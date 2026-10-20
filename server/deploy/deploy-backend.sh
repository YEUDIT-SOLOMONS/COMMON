#!/usr/bin/env bash
# פריסת השרת (photorental-api) מתוך Google Cloud Shell, כולל מפתח ההצפנה של מספרי חשבון הבנק.
# המפתח נוצר כאן בתוך Google ולעולם לא מוצג על המסך ולא עובר דרך המחשב המקומי (NetFree).
set -euo pipefail

PROJECT=rent-photo
SERVICE=photorental-api
SECRET=payout-encryption-key

gcloud config set project "$PROJECT" >/dev/null

REGION=$(gcloud run services list --filter="metadata.name=$SERVICE" \
  --format="value(metadata.labels.'cloud.googleapis.com/location')" | head -1)
if [ -z "$REGION" ]; then echo "לא נמצא שירות $SERVICE בפרויקט $PROJECT" >&2; exit 1; fi
echo "== השרת רץ באזור: $REGION"

gcloud services enable secretmanager.googleapis.com >/dev/null

# יצירת המפתח רק אם עוד לא קיים - לעולם לא מחליפים מפתח קיים (אחרת אי אפשר לפענח נתונים ישנים)
if gcloud secrets describe "$SECRET" >/dev/null 2>&1; then
  echo "== המפתח $SECRET כבר קיים - משתמשים בו"
else
  openssl rand -base64 32 | tr -d '\n' | gcloud secrets create "$SECRET" --data-file=- --replication-policy=automatic
  echo "== נוצר מפתח הצפנה חדש ($SECRET)"
fi

SA=$(gcloud run services describe "$SERVICE" --region "$REGION" --format="value(spec.template.spec.serviceAccountName)")
if [ -z "$SA" ]; then
  SA="$(gcloud projects describe "$PROJECT" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"
fi
echo "== מרשה לשרת ($SA) לקרוא את המפתח"
gcloud secrets add-iam-policy-binding "$SECRET" \
  --member="serviceAccount:$SA" --role=roles/secretmanager.secretAccessor >/dev/null

# סודות נוספים (מפתח התחברות, Resend, GetPackage) נשמרים ב-Secret Manager ולא בקובץ appsettings.json.
# אם jwt-key עוד לא קיים - מריצים את setup-secrets.sh (הוא יזהה ויבקש את המפתחות).
if ! gcloud secrets describe jwt-key >/dev/null 2>&1; then
  bash "$(dirname "$0")/setup-secrets.sh"
fi
SECRETS="Encryption__PayoutKey=$SECRET:latest,Jwt__Key=jwt-key:latest,Scheduler__Key=SCHEDULER_KEY:latest"
for pair in "resend-api-key:Resend__ApiKey" "getpackage-api-key:GetPackage__ApiKey" "summit-company-id:Summit__CompanyId" "summit-api-key:Summit__ApiKey" "summit-public-key:Summit__PublicKey"; do
  name="${pair%%:*}"; env="${pair##*:}"
  if gcloud secrets describe "$name" >/dev/null 2>&1; then
    SECRETS="$SECRETS,$env=$name:latest"
  fi
done
for name in SCHEDULER_KEY jwt-key resend-api-key getpackage-api-key summit-company-id summit-api-key summit-public-key; do
  if gcloud secrets describe "$name" >/dev/null 2>&1; then
    gcloud secrets add-iam-policy-binding "$name"       --member="serviceAccount:$SA" --role=roles/secretmanager.secretAccessor >/dev/null
  fi
done

# שירותי הרקע (תזכורות החזרה, "הפריט נאסף?", דוח עמלה חודשי) מופעלים כל 5 דקות מפונקציית ענן של Firebase
# (deploy-scheduler.sh) שמעירה את השרת - ולכן אין צורך באינסטנציה פעילה תמיד. הדגלים למטה מבטיחים שלא
# תישאר אינסטנציה קבועה (שעולה כסף) מפריסה קודמת.

echo "== פורס את השרת (כמה דקות)..."
# --update-secrets מוסיף רק את המפתח החדש ומשאיר את כל שאר ההגדרות הקיימות של השירות כמו שהן
gcloud run deploy "$SERVICE" --source . --region "$REGION" \
  --update-secrets="$SECRETS" --update-env-vars="Resend__FromName=common,Resend__FromEmail=common@onyxapp.co.il,Resend__ReplyTo=common@onyxapp.co.il" \
  --min-instances=0 --cpu-throttling --quiet

URL=$(gcloud run services describe "$SERVICE" --region "$REGION" --format="value(status.url)")
echo
echo "== בדיקות:"
echo "   /api/users בלי התחברות (צריך 401): $(curl -s -o /dev/null -w '%{http_code}' "$URL/api/users")"
echo "   /api/categories (צריך 200): $(curl -s -o /dev/null -w '%{http_code}' "$URL/api/categories")"
sleep 20
if gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=$SERVICE AND textPayload:\"Encryption:PayoutKey\"" \
     --freshness=10m --limit=1 --format="value(textPayload)" | grep -q .; then
  echo "   !! אזהרה: השרת דיווח שמפתח ההצפנה לא הוגדר"
else
  echo "   מפתח ההצפנה נטען (אין אזהרה ביומן)"
fi
echo "== סיום"
