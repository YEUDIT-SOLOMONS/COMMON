#!/usr/bin/env bash
# בדיקת חיבור ל-GetPackage בלי להזמין שליח אמיתי. מריצים ב-Cloud Shell:  bash test-getpackage.sh
#   שלב 1: בודק שהשרת ב-Cloud Run באמת מחובר למפתח GetPackage (אחרת הוא עובד במצב מדומה)
#   שלב 2: הצעות מחיר (רגיל + אקספרס) - קריאות קריאה בלבד, לא יוצרות משלוח ולא מחייבות
#   שלב 3 (רק באישור שלך): "משלוח יבש" עם isDummy=true - לבדיקה שאפשר לפתוח משלוח בהצלחה
# המפתח נקרא מ-Secret Manager ולא מוצג על המסך.
set -euo pipefail

PROJECT=rent-photo
SERVICE=photorental-api
REGION=me-west1
BASE_URL="${GETPACKAGE_BASE_URL:-https://api.getpackage.com}"

gcloud config set project "$PROJECT" >/dev/null

echo "== שלב 1: האם השרת מחובר למפתח האמיתי?"
if gcloud secrets describe getpackage-api-key >/dev/null 2>&1; then
  echo "   הסוד getpackage-api-key קיים ב-Secret Manager"
else
  echo "   !! הסוד getpackage-api-key לא קיים - הריצי קודם: bash setup-secrets.sh ואז bash deploy-backend.sh"
  exit 1
fi
if gcloud run services describe "$SERVICE" --region "$REGION" --format=json 2>/dev/null | grep -q 'GetPackage__ApiKey'; then
  echo "   השירות ב-Cloud Run מחובר למפתח - המשלוחים אמיתיים"
else
  echo "   !! השירות ב-Cloud Run לא מחובר למפתח - כרגע המשלוחים מדומים. הריצי bash deploy-backend.sh"
fi

KEY="$(gcloud secrets versions access latest --secret=getpackage-api-key)"
if [ -z "$KEY" ]; then echo "המפתח ריק"; exit 1; fi

ask() { # ask "טקסט" "ברירת מחדל" -> מדפיס את התשובה
  local reply
  read -r -p "$1 [$2]: " reply
  printf '%s' "${reply:-$2}"
}

echo
echo "== כתובות לבדיקה (Enter = ברירת המחדל; עדיף כתובות אמיתיות בישראל)"
P_CITY="$(ask 'איסוף - עיר' 'ירושלים')"
P_STREET="$(ask 'איסוף - רחוב' 'יפו')"
P_NUMBER="$(ask 'איסוף - מספר' '1')"
D_CITY="$(ask 'מסירה - עיר' 'בני ברק')"
D_STREET="$(ask 'מסירה - רחוב' 'רבי עקיבא')"
D_NUMBER="$(ask 'מסירה - מספר' '1')"
DATE="$(date -d 'tomorrow' +%Y-%m-%d)"

# בונה את גוף הבקשה (JSON תקין גם עם עברית וגרשיים) לפי ההגדרות ב-GetPackageShippingService.cs
export P_CITY P_STREET P_NUMBER D_CITY D_STREET D_NUMBER DATE
build() { # build quote|express|create|dummy
  python3 - "$1" <<'PY'
import json, os, sys
kind = sys.argv[1]
pick = {"address": {"country": "IL", "city": os.environ["P_CITY"], "street": os.environ["P_STREET"], "number": os.environ["P_NUMBER"]}}
drop = {"address": {"country": "IL", "city": os.environ["D_CITY"], "street": os.environ["D_STREET"], "number": os.environ["D_NUMBER"]}}
pkg = {"size": "MEDIUM"}
if kind == "quote":
    body = {"pickUpPoint": pick, "dropOffPoint": drop, "package": pkg}
elif kind == "express":
    body = {"deliveries": [{"pickUpPoint": pick, "dropOffPoint": drop}], "stopPointsOrder": [0, 0]}
else:
    body = {"pickUpPoint": pick, "dropOffPoint": drop, "package": pkg, "date": os.environ["DATE"]}
    if kind == "dummy":
        body["isDummy"] = True
        body["notes"] = "בדיקה - קומה 3 בכתובת המסירה"
print(json.dumps(body, ensure_ascii=False))
PY
}

call() { # call PATH BODY -> מדפיס גוף תשובה ואז שורת סטטוס
  curl -sS -w '\nHTTP_STATUS:%{http_code}\n' -X POST "$BASE_URL$1" \
    -H "Authorization: APIKEY $KEY" -H "X-API-Version: 1.4" \
    -H "Content-Type: application/json; charset=utf-8" --data-binary "$2"
}

show() { # show NAME RESPONSE
  local status body
  status="$(printf '%s' "$2" | sed -n 's/^HTTP_STATUS://p')"
  body="$(printf '%s' "$2" | sed '/^HTTP_STATUS:/d')"
  echo "   [$1] סטטוס HTTP: $status"
  printf '%s' "$body" | python3 -c '
import json, sys
raw = sys.stdin.read()
try:
    d = json.loads(raw)
    keys = {k: d[k] for k in ("id", "totalRate", "senderTrackingUrl", "isDummy", "message", "error", "errors") if k in d}
    print("   " + json.dumps(keys or d, ensure_ascii=False)[:600])
except Exception:
    print("   " + raw[:600])'
  [ "$status" = "200" ] || [ "$status" = "201" ]
}

echo
echo "== שלב 2: הצעות מחיר (לא יוצר משלוח)"
OK=1
show "משלוח רגיל" "$(call /v1/deliveries/sharedRoute/quote "$(build quote)")" || OK=0
show "משלוח אקספרס" "$(call /v1/deliveries/express/quote "$(build express)")" || OK=0

if [ "$OK" != "1" ]; then
  echo
  echo "!! הצעות המחיר נכשלו - לא ממשיכים. בדקי את המפתח (ייתכן שפג תוקפו) ואת הכתובות."
  exit 1
fi
echo "   הצעות המחיר עובדות: המפתח תקין והחיבור ל-GetPackage פעיל"

echo
echo "== שלב 3 (אופציונלי): משלוח יבש לבדיקת פתיחת משלוח"
echo "   נשלח עם isDummy=true ('Dry-run for testing only' לפי ה-OpenAPI הרשמי של GetPackage) - לא מזמין שליח."
echo "   נשלחת גם הערה לשליח (קומה) כדי לוודא שהשדה מתקבל. אם בכל זאת ייפתח משלוח אמיתי - בטלי אותו בלוח הבקרה."
read -r -p "להריץ את המשלוח היבש? (כתבי yes להמשך, אחרת Enter לסיום): " CONFIRM
if [ "$CONFIRM" != "yes" ]; then
  echo "   דילגנו על שלב 3. הצעות המחיר מספיקות כדי לדעת שהחיבור עובד."
  exit 0
fi

if show "משלוח יבש" "$(call /v1/deliveries/sharedRoute "$(build dummy)")"; then
  echo "   נפתח משלוח יבש. אם בתשובה למעלה מופיע isDummy: true - לא הוזמן שליח."
  echo "   אם אין isDummy בתשובה, בדקי בלוח הבקרה של GetPackage ובטלי משלוח שנפתח בטעות."
else
  echo "!! פתיחת המשלוח היבש נכשלה - ראי את ההודעה למעלה."
  exit 1
fi
