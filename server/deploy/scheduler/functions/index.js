// פונקציית ענן שרצה כל 5 דקות (Cloud Scheduler של Firebase) וקוראת לשרת של common, כדי שיריץ את המשימות
// המתוזמנות: תזכורות החזרה, "הפריט נאסף?" ודוח עמלה חודשי. הלוגיקה עצמה נשארת בשרת (.NET) - כאן רק מעירים אותו.
// המפתח הסודי המשותף (SCHEDULER_KEY) נשמר ב-Secret Manager, וזהה לזה שהשרת בודק.
const { onSchedule } = require('firebase-functions/v2/scheduler')
const { defineSecret } = require('firebase-functions/params')

const schedulerKey = defineSecret('SCHEDULER_KEY')
const API_URL =
  process.env.COMMON_API_URL || 'https://photorental-api-826241029359.me-west1.run.app/api/internal/run-scheduled'

exports.runScheduled = onSchedule(
  {
    schedule: 'every 5 minutes',
    timeZone: 'Asia/Jerusalem',
    region: 'europe-west1',
    secrets: [schedulerKey],
    timeoutSeconds: 300,
    retryCount: 0,
  },
  async () => {
    const response = await fetch(API_URL, {
      method: 'POST',
      headers: { 'X-Scheduler-Key': schedulerKey.value() },
      signal: AbortSignal.timeout(280000),
    })
    if (!response.ok) {
      throw new Error(`common API answered ${response.status}`)
    }
  },
)
