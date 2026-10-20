#!/usr/bin/env bash
# בדיקת חיבור לסאמיט (SUMIT) מתוך Google Cloud Shell: יוצר דף תשלום לבדיקה על 1 ש"ח ומדפיס את הכתובת שלו.
# לא מחייב כלום - התשלום מתבצע רק אם פותחים את הכתובת ומשלמים. המפתחות נקראים מ-Secret Manager ולא מוצגים.
# אם מודפסת כתובת של דף תשלום - החיבור והמפתחות תקינים. אחרת מודפסת השגיאה של סאמיט.
set -euo pipefail
gcloud config set project rent-photo >/dev/null

COMPANY=$(gcloud secrets versions access latest --secret=summit-company-id)
KEY=$(gcloud secrets versions access latest --secret=summit-api-key)

RESPONSE=$(curl -s -X POST "https://api.sumit.co.il/billing/payments/beginredirect/" \
  -H "Content-Type: application/json" \
  -d "{\"Credentials\":{\"CompanyID\":${COMPANY},\"APIKey\":\"${KEY}\"},\"Customer\":{\"Name\":\"בדיקה\"},\"Items\":[{\"Item\":{\"Name\":\"בדיקת חיבור common\"},\"Quantity\":1,\"UnitPrice\":1}],\"VATIncluded\":true,\"RedirectURL\":\"https://commonrent.web.app/commission-paid\",\"ExternalIdentifier\":\"test-connection\"}")
unset KEY

echo "$RESPONSE" | python3 -c "
import json,sys
r=json.load(sys.stdin)
url=(r.get('Data') or {}).get('RedirectURL')
if url:
    print('הצלחה! כתובת דף התשלום לבדיקה:'); print(url)
else:
    print('נכשל:', r.get('UserErrorMessage') or r.get('TechnicalErrorDetails') or r)
"
