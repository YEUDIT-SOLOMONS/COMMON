#!/usr/bin/env bash
# בדיקת חלונות ההגעה של GetPackage (קריאה בלבד, עולה עשירית יחידה, לא יוצרת משלוח). המפתח נקרא מ-Secret Manager ולא מוצג.
# מריצים ב-Cloud Shell:  bash probe-servicetimes.sh
set -uo pipefail
gcloud config set project rent-photo >/dev/null
KEY="$(gcloud secrets versions access latest --secret=getpackage-api-key)"
BASE="${GETPACKAGE_BASE_URL:-https://api.getpackage.com}"

call() {
  local name="$1" body="$2" code
  code="$(curl -s -m 40 -o /tmp/gp_st.txt -w '%{http_code}' -X POST "$BASE/v1/deliveries/serviceTimes" \
    -H "Authorization: APIKEY $KEY" -H "Content-Type: application/json" -H "X-API-Version: 1.4" -d "$body")"
  printf '%s\nHTTP %s\n%s\n\n' "$name" "$code" "$(head -c 600 /tmp/gp_st.txt | tr '\n' ' ')"
}

ADDR='{"country":"IL","city":"בני ברק","street":"רבי עקיבא","number":"1"}'
for offset in 1 2 3; do
  DAY="$(date -d "+$offset day" +%Y-%m-%d)"
  call "משלוח רגיל, בני ברק, $DAY" "{\"address\":$ADDR,\"dateFrom\":\"$DAY\",\"dateTo\":\"$DAY\",\"serviceType\":\"SDD\",\"subServiceType\":\"GENERAL\"}"
done

DAY="$(date -d '+1 day' +%Y-%m-%d)"
call "בלי subServiceType" "{\"address\":$ADDR,\"dateFrom\":\"$DAY\",\"dateTo\":\"$DAY\",\"serviceType\":\"SDD\"}"
call "עיר בלבד" "{\"address\":{\"country\":\"IL\",\"city\":\"בני ברק\"},\"dateFrom\":\"$DAY\",\"dateTo\":\"$DAY\",\"serviceType\":\"SDD\",\"subServiceType\":\"GENERAL\"}"
