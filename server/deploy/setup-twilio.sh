#!/usr/bin/env bash
# חיבור השרת (photorental-api) למרכזיית Twilio לחיוג טלפוני אמיתי (אימות זמינות מול המשכיר).
# מריצים ב-Google Cloud Shell. הפרטים נשאלים כאן ונשמרים ישירות בתוך Google (האסימון ב-Secret Manager) -
# הם לא עוברים דרך המחשב המקומי ולא נשמרים בשום קובץ.
set -euo pipefail

PROJECT=rent-photo
SERVICE=photorental-api
SECRET=twilio-auth-token

gcloud config set project "$PROJECT" >/dev/null

REGION=$(gcloud run services list --filter="metadata.name=$SERVICE" \
  --format="value(metadata.labels.'cloud.googleapis.com/location')" | head -1)
if [ -z "$REGION" ]; then echo "לא נמצא שירות $SERVICE בפרויקט $PROJECT" >&2; exit 1; fi
URL=$(gcloud run services describe "$SERVICE" --region "$REGION" --format="value(status.url)")
echo "== השרת: $URL (אזור $REGION)"

read -r -p "Twilio Account SID (מתחיל ב-AC): " SID
read -r -s -p "Twilio Auth Token (לא יוצג על המסך): " TOKEN; echo
read -r -p "מספר המתקשר בפורמט בינלאומי, למשל +972501234567 (מספר Twilio או Verified Caller ID): " FROM

gcloud services enable secretmanager.googleapis.com >/dev/null
# אם ההדבקה בשדה הסמוי לא עובדת: אפשר ליצור את הסוד twilio-auth-token ב-Secret Manager בדפדפן
# (Google Cloud Console -> Secret Manager -> Create Secret) ולהקיש כאן Enter בלי להקליד כלום
if [ -n "$TOKEN" ]; then
  if gcloud secrets describe "$SECRET" >/dev/null 2>&1; then
    printf '%s' "$TOKEN" | gcloud secrets versions add "$SECRET" --data-file=- >/dev/null
    echo "== עודכן הסוד $SECRET"
  else
    printf '%s' "$TOKEN" | gcloud secrets create "$SECRET" --data-file=- --replication-policy=automatic >/dev/null
    echo "== נוצר הסוד $SECRET"
  fi
elif gcloud secrets describe "$SECRET" >/dev/null 2>&1; then
  echo "== משאירים את הסוד הקיים $SECRET"
else
  echo "!! לא הוזן אסימון והסוד $SECRET לא קיים. צרי אותו ב-Secret Manager או הריצי שוב והדביקי את האסימון." >&2
  exit 1
fi
unset TOKEN

SA=$(gcloud run services describe "$SERVICE" --region "$REGION" --format="value(spec.template.spec.serviceAccountName)")
if [ -z "$SA" ]; then
  SA="$(gcloud projects describe "$PROJECT" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"
fi
gcloud secrets add-iam-policy-binding "$SECRET" \
  --member="serviceAccount:$SA" --role=roles/secretmanager.secretAccessor >/dev/null

echo "== מעדכן את השרת (בלי בנייה מחדש)..."
# PublicBaseUrl חייב להיות כתובת השרת עצמה - Twilio קורא אליה בחזרה (Webhook) עם הספרה שהמשכיר לחץ,
# והשרת מאמת את חתימת Twilio מול הכתובת הזו
gcloud run services update "$SERVICE" --region "$REGION" \
  --update-env-vars "Twilio__AccountSid=$SID,Twilio__FromPhoneNumber=$FROM,Twilio__PublicBaseUrl=$URL" \
  --update-secrets "Twilio__AuthToken=$SECRET:latest" --quiet

echo "== סיום. מעכשיו 'אימות זמינות מול חבר הקהילה' מחייג בפועל למספר הטלפון של בעל הפריט."
