#!/usr/bin/env bash
# אבחון שני ל-GetPackage (הצעות מחיר בלבד, לא יוצר משלוח ולא מחייב): מחפש דרך לגרום לאקספרס לעבוד.
# בבדיקה הקודמת כל ערך של package.size נדחה אצלם ("Enum PackageSize cannot represent non-string value") וגם בלי חבילה
# ("החבילה לא צריכה להיות ריקה"). כאן בודקים חבילה בלי size, עם packageId בלבד, ועוד וריאציות.
# מריצים ב-Cloud Shell:  bash probe-getpackage2.sh   (המפתח נקרא מ-Secret Manager ולא מוצג)
set -uo pipefail
gcloud config set project rent-photo >/dev/null
KEY="$(gcloud secrets versions access latest --secret=getpackage-api-key)"
BASE="${GETPACKAGE_BASE_URL:-https://api.getpackage.com}"

call() { # call "<name>" "<path>" "<json>"
  local name="$1" path="$2" body="$3" out
  out="$(curl -s -m 40 -o /tmp/gp_out.txt -w '%{http_code}' -X POST "$BASE$path" \
    -H "Authorization: APIKEY $KEY" -H "Content-Type: application/json" -H "X-API-Version: 1.4" -d "$body")"
  printf '%-44s HTTP %s  %s\n' "$name" "$out" "$(head -c 260 /tmp/gp_out.txt | tr '\n' ' ')"
}

addr() { printf '{"address":{"country":"IL","city":"%s","street":"%s","number":"%s"}}' "$1" "$2" "$3"; }
JER="$(addr ירושלים יפו 1)"
BB1="$(addr 'בני ברק' 'רבי עקיבא' 1)"

q() { # q "<שם>" '<package json fragment, ריק = בלי package>'
  local pkg=""
  [ -n "$2" ] && pkg=",\"package\":$2"
  call "$1" /v1/deliveries/express/quote "{\"deliveries\":[{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1$pkg}],\"stopPointsOrder\":[0,0]}"
}

echo "== אקספרס - וריאציות של חבילה (ירושלים -> בני ברק)"
q "package = {packageId}" '{"packageId":"p1"}'
q "package = {} (ריק)" '{}'
q "package = {size: null}" '{"size":null}'
q "package = {size: 2} (מספר)" '{"size":2}'
q "package = {size: \"2\"}" '{"size":"2"}'
q "package = {packageId, size: null}" '{"packageId":"p1","size":null}'

echo
echo "== אקספרס - תאריך/שעה מפורשים"
TOMORROW="$(date -d 'tomorrow 10:00' -u +%Y-%m-%dT%H:%M:%S.000Z)"
call "עם date + packageId" /v1/deliveries/express/quote "{\"date\":\"$TOMORROW\",\"deliveries\":[{\"pickUpPoint\":$JER,\"dropOffPoint\":$BB1,\"package\":{\"packageId\":\"p1\"}}],\"stopPointsOrder\":[0,0]}"

unset KEY
echo
echo "== סיום. אפשר להעתיק את כל הפלט ולשלוח אליי."
