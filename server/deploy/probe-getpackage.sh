#!/usr/bin/env bash
# אבחון GetPackage (קריאות הצעת מחיר בלבד - לא יוצר משלוח ולא מחייב). מריצים ב-Cloud Shell:  bash probe-getpackage.sh
# בודק כמה וריאציות כדי לדעת למה הצעת מחיר נדחית:
#   א. משלוח רגיל (Shared Route) בתוך עיר - אם עובד, הבעיה בבדיקה הקודמת הייתה מרחק (ירושלים-בני ברק רחוק מדי למסלול משותף)
#   ב. אקספרס עם גודל חבילה בכל ארבעת הערכים - כדי לראות איזה ערך GetPackage מקבלים
#   ג. "מרחק ארוך" (Long Distance) לירושלים-בני ברק
# המפתח נקרא מ-Secret Manager ולא מוצג.
set -uo pipefail
gcloud config set project rent-photo >/dev/null
KEY="$(gcloud secrets versions access latest --secret=getpackage-api-key)"
BASE="${GETPACKAGE_BASE_URL:-https://api.getpackage.com}"

call() { # call "<name>" "<api-version>" "<path>" "<json>"
  local name="$1" ver="$2" path="$3" body="$4" out code
  out="$(curl -s -m 40 -o /tmp/gp_out.txt -w '%{http_code}' -X POST "$BASE$path" \
    -H "Authorization: APIKEY $KEY" -H "Content-Type: application/json" ${ver:+-H "X-API-Version: $ver"} -d "$body")"
  printf '%-46s HTTP %s  %s\n' "$name" "$out" "$(head -c 230 /tmp/gp_out.txt | tr '\n' ' ')"
}

addr() { printf '{"address":{"country":"IL","city":"%s","street":"%s","number":"%s"}}' "$1" "$2" "$3"; }
JER="$(addr ירושלים יפו 1)"
BB1="$(addr 'בני ברק' 'רבי עקיבא' 1)"
BB2="$(addr 'בני ברק' "ז'בוטינסקי" 10)"

echo "== א. משלוח רגיל (Shared Route)"
call "בתוך בני ברק" 1.4 /v1/deliveries/sharedRoute/quote "{\"pickUpPoint\":$BB1,\"dropOffPoint\":$BB2,\"package\":{\"size\":\"MEDIUM\"}}"
call "ירושלים -> בני ברק" 1.4 /v1/deliveries/sharedRoute/quote "{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"size\":\"MEDIUM\"}}"

echo
echo "== ב. אקספרס - גדלי חבילה (ירושלים -> בני ברק)"
for size in ENVELOPE SMALL MEDIUM LARGE; do
  call "אקספרס size=$size (גרסה 1.4)" 1.4 /v1/deliveries/express/quote "{\"deliveries\":[{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"size\":\"$size\"}}],\"stopPointsOrder\":[0,0]}"
done
call "אקספרס size=MEDIUM (בלי כותרת גרסה)" "" /v1/deliveries/express/quote "{\"deliveries\":[{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"size\":\"MEDIUM\"}}],\"stopPointsOrder\":[0,0]}"
call "אקספרס עם packageId + size" 1.4 /v1/deliveries/express/quote "{\"deliveries\":[{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"packageId\":\"p1\",\"size\":\"MEDIUM\"}}],\"stopPointsOrder\":[0,0]}"
call "אקספרס בתוך בני ברק" 1.4 /v1/deliveries/express/quote "{\"deliveries\":[{\"pickUpPoint\":$BB1,\"dropOffPoint\":$BB2,\"package\":{\"size\":\"MEDIUM\"}}],\"stopPointsOrder\":[0,0]}"

echo
echo "== ג. מרחק ארוך (Long Distance)"
call "ירושלים -> בני ברק (בלי branch)" 1.4 /v1/deliveries/longDistance/quote "{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"size\":\"MEDIUM\"}}"

echo
echo "== ד. הערכת השירות המתאים לפי GetPackage (serviceTimes/evaluate - הדרך המומלצת שלהם לבחור בין רגיל / מרחק ארוך / אקספרס)"
PKG='"package":{"size":"MEDIUM"}'
call "evaluate ירושלים -> בני ברק (הכי מתאים)" 1.4 /v1/deliveries/serviceTimes/evaluate "{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,$PKG}"
call "evaluate בתוך בני ברק (הכי מתאים)" 1.4 /v1/deliveries/serviceTimes/evaluate "{\"pickUpPoint\":$BB1,\"dropOffPoint\":$BB2,$PKG}"
call "evaluate ירושלים -> בני ברק, LONG_LEG" 1.4 /v1/deliveries/serviceTimes/evaluate "{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,$PKG,\"serviceType\":\"SDD\",\"subServiceType\":\"LONG_LEG\"}"
call "evaluate ירושלים -> בני ברק, EXPRESS" 1.4 /v1/deliveries/serviceTimes/evaluate "{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,$PKG,\"serviceType\":\"EXPRESS\"}"

echo
echo "== ה. יישובים זמינים למשלוח רגיל (sharedRoute/settlements)"
for city in "בני ברק" "ירושלים"; do
  out="$(curl -s -m 40 -o /tmp/gp_out.txt -w '%{http_code}' -G "$BASE/v1/deliveries/sharedRoute/settlements" --data-urlencode "settlementName=$city" -H "Authorization: APIKEY $KEY" -H "X-API-Version: 1.4")"
  printf '%-46s HTTP %s  %s
' "יישובים קרובים ל-$city" "$out" "$(head -c 400 /tmp/gp_out.txt | tr '
' ' ')"
done

unset KEY
echo
echo "== סיום. אפשר להעתיק את כל הפלט ולשלוח אליי."
