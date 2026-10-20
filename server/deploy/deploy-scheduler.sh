#!/usr/bin/env bash
# פורס את פונקציית הענן שמעירה את השרת כל 5 דקות (תזכורות, "הפריט נאסף?", דוח עמלה חודשי).
# מריצים ב-Cloud Shell, מתוך התיקייה שנפתחה מה-zip, אחרי deploy-backend.sh:   bash deploy-scheduler.sh
# דורש: פרויקט Firebase בתוכנית Blaze (תשלום לפי שימוש; כאן העלות כמעט אפס), והסוד SCHEDULER_KEY
# (נוצר אוטומטית ב-setup-secrets.sh ומחובר לשרת ב-deploy-backend.sh).
set -euo pipefail

PROJECT=rent-photo
gcloud config set project "$PROJECT" >/dev/null

if ! gcloud secrets describe SCHEDULER_KEY >/dev/null 2>&1; then
  echo "!! הסוד SCHEDULER_KEY לא קיים. הריצי קודם bash setup-secrets.sh ואז bash deploy-backend.sh"; exit 1
fi

gcloud services enable cloudfunctions.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com \
  cloudscheduler.googleapis.com run.googleapis.com eventarc.googleapis.com secretmanager.googleapis.com >/dev/null

cd "$(dirname "$0")/scheduler"
(cd functions && npm install --no-audit --no-fund --silent)

# Firebase CLI צריך התחברות משלו (נפרדת מ-gcloud). אם עוד לא מחוברת - נפתח כאן תהליך התחברות עם קוד להדבקה
if ! npx --yes firebase-tools@latest projects:list >/dev/null 2>&1; then
  echo "== צריך להתחבר ל-Firebase: ייפתח קישור, מתחברים עם חשבון Google ומדביקים כאן את הקוד שמתקבל"
  npx --yes firebase-tools@latest login --no-localhost
fi

echo "== פורס את פונקציית התזמון (כמה דקות)..."
npx --yes firebase-tools@latest deploy --only functions --project "$PROJECT" --force

echo "== בדיקה: קריאה ידנית אחת לשרת"
KEY="$(gcloud secrets versions access latest --secret=SCHEDULER_KEY)"
URL="$(gcloud run services describe photorental-api --region me-west1 --format='value(status.url)')"
echo "   עם מפתח (צריך 200): $(curl -s -o /dev/null -w '%{http_code}' -X POST -H "X-Scheduler-Key: $KEY" "$URL/api/internal/run-scheduled")"
echo "   בלי מפתח (צריך 404): $(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/api/internal/run-scheduled")"
echo "== סיום. מעכשיו הפונקציה רצה כל 5 דקות. לוגים: firebase functions:log"
