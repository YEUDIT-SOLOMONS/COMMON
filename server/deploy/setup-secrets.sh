#!/usr/bin/env bash
# יוצר ב-Secret Manager את הסודות של השרת (במקום לשמור אותם בקובץ appsettings.json):
#   jwt-key            - מפתח חתימת ההתחברות. נוצר כאן אוטומטית (חדש, אקראי) אם עוד לא קיים
#   resend-api-key     - מפתח Resend לשליחת מיילים
#   getpackage-api-key - מפתח GetPackage לשליחויות
#   summit-company-id / summit-api-key / summit-public-key - SUMIT (סאמיט): תשלום עמלה באשראי בדוח החודשי
# המפתחות נשאלים כאן ב-Cloud Shell (לא יוצגו על המסך) ונשמרים ישירות בתוך Google.
# מומלץ להדביק מפתחות חדשים (שהופקו מחדש באתרי הספקים), כי המפתחות הישנים הופיעו בקובץ בקוד.
# Enter ריק = משאירים את הסוד הקיים (אם יש), או מדלגים (השירות יעבוד במצב מדומה / מושבת).
# מופעל אוטומטית מתוך deploy-backend.sh כשחסר jwt-key; אפשר גם להריץ ידנית כדי להחליף מפתח.
set -euo pipefail

PROJECT=rent-photo
gcloud config set project "$PROJECT" >/dev/null
gcloud services enable secretmanager.googleapis.com >/dev/null

# יוצר/מעדכן סוד מערך שהתקבל; לא נוגע בסוד קיים כשהערך ריק
put_secret() {
  local name="$1" value="$2"
  if [ -z "$value" ]; then return 0; fi
  if gcloud secrets describe "$name" >/dev/null 2>&1; then
    printf '%s' "$value" | gcloud secrets versions add "$name" --data-file=- >/dev/null
    echo "== עודכן הסוד $name"
  else
    printf '%s' "$value" | gcloud secrets create "$name" --data-file=- --replication-policy=automatic >/dev/null
    echo "== נוצר הסוד $name"
  fi
}

# --- מפתח התחברות: נוצר אוטומטית, רק אם עוד לא קיים (החלפה מנתקת את כל המשתמשות המחוברות) ---
if gcloud secrets describe jwt-key >/dev/null 2>&1; then
  echo "== jwt-key כבר קיים - משאירים אותו"
else
  put_secret jwt-key "$(openssl rand -base64 64 | tr -d '\n')"
fi

# מפתח משותף לפונקציית התזמון (deploy-scheduler.sh) ולשרת - נוצר אוטומטית אם עוד לא קיים
if gcloud secrets describe SCHEDULER_KEY >/dev/null 2>&1; then
  echo "== SCHEDULER_KEY כבר קיים - משאירים אותו"
else
  put_secret SCHEDULER_KEY "$(openssl rand -hex 32)"
fi

read -r -s -p "מפתח Resend (מתחיל ב-re_; Enter = להשאיר/לדלג): " RESEND; echo
put_secret resend-api-key "$RESEND"; unset RESEND

read -r -s -p "מפתח GetPackage (Enter = להשאיר/לדלג): " GETPACKAGE; echo
put_secret getpackage-api-key "$GETPACKAGE"; unset GETPACKAGE

read -r -p "SUMIT - מזהה העסק (מספר; Enter = להשאיר/לדלג): " SM_COMPANY
put_secret summit-company-id "$SM_COMPANY"; unset SM_COMPANY
read -r -s -p "SUMIT - מפתח התחברות פרטי (לא יוצג; Enter = להשאיר/לדלג): " SM_KEY; echo
put_secret summit-api-key "$SM_KEY"; unset SM_KEY
read -r -s -p "SUMIT - מפתח התחברות ציבורי (Enter = להשאיר/לדלג): " SM_PUBLIC; echo
put_secret summit-public-key "$SM_PUBLIC"; unset SM_PUBLIC

echo "== הסודות מוכנים. deploy-backend.sh יחבר אותם לשרת."
