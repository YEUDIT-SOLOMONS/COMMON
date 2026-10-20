# COMMON – קהילת הצלמות להשכרת ומכירת ציוד צילום

אתר חי: **https://commonrent.web.app**

COMMON היא פלטפורמה קהילתית שבה צלמות משכירות זו לזו ציוד צילום, אביזרי סט ולוקיישנים, ומוכרות ציוד יד 2.
האתר מטפל בכל התהליך: אימות זמינות בשיחה טלפונית אוטומטית למשכירה, הזמנה ותשלום, משלוח עם שליח,
תזכורות החזרה, שיחות מתווכות בין הצדדים בלי לחשוף מספרי טלפון, ביקורות ודוח עמלות חודשי.

## מבנה התיקיות

```
PhotoRental/
├── client/                 אתר הלקוח – React 19 + Vite
│   ├── src/
│   │   ├── api/            קריאות לשרת (endpoints.js, client.js)
│   │   ├── components/     רכיבים (חלון השכרה, חלון קנייה, כרטיסי פריט...)
│   │   ├── pages/          עמודים (בית, יד 2, אזור אישי, ניהול, תקנון...)
│   │   ├── context/        התחברות ומצב משתמשת
│   │   └── utils/          עזרים (תאריכים, טלפון, מע"מ...)
│   ├── public/             קבצים סטטיים (לוגו, תמונות שיתוף)
│   └── firebase.json       הגדרות Firebase Hosting
│
├── server/                 השרת – ASP.NET Core 9 (C#)
│   ├── PhotoRental.API/        Controllers, שירותי רקע, אבטחה, Program.cs
│   ├── PhotoRental.Service/    הלוגיקה העסקית (השכרות, מכירות, שיחות, משלוחים, תשלומים)
│   ├── PhotoRental.Data/       גישה ל-Firestore (Repositories)
│   ├── PhotoRental.Core/       ישויות, DTOs, ממשקים ועזרים משותפים
│   ├── deploy/                 סקריפטים לפריסה מ-Google Cloud Shell ו-Cloud Scheduler
│   ├── Dockerfile              בניית תמונת השרת ל-Cloud Run
│   └── PhotoRental.sln
│
└── marketing/              חומרי שיווק, מודעות ודף לוקיישנים
```

## טכנולוגיות ושירותים

| תחום | שירות |
|---|---|
| אתר | React 19, Vite, React Router |
| שרת | ASP.NET Core 9, Serilog |
| מסד נתונים | Google Firestore (דרך REST) |
| אחסון ופריסה | Firebase Hosting (אתר), Google Cloud Run (שרת), Secret Manager (סודות) |
| משימות מתוזמנות | Google Cloud Scheduler (כל 5 דקות) |
| שיחות טלפון | Twilio (כולל זיהוי תא קולי וקול Google He-IL) |
| מיילים | Resend |
| תשלומים באשראי | SUMIT (סאמיט) |
| משלוחים | GetPackage (רגיל עד 20 ק"מ, אקספרס בין-עירוני) |

## הרצה מקומית

### שרת

דרישות: .NET 9 SDK, ומפתח שירות של Firebase.

1. שמרי את מפתח השירות של Firebase בקובץ `server/PhotoRental.API/firebase-key.json`.
   הקובץ סודי, והוא לא נכנס לגיט.
2. העתיקי את `appsettings.Development.example.json` לקובץ `appsettings.Development.json`, ומלאי בו מפתח JWT.
3. הריצי:

```bash
cd server
dotnet run --project PhotoRental.API
```

השרת עולה בכתובת `http://localhost:5116` (Swagger: `/swagger`).
כל עוד לא הוגדרו מפתחות לשירותים החיצוניים (Twilio, Resend, SUMIT, GetPackage), השירותים האלה עובדים במצב מדומה או מושבתים.

### אתר

דרישות: Node.js 20 ומעלה.

```bash
cd client
cp .env.example .env
npm install
npm run dev
```

האתר עולה בכתובת `http://localhost:5173`.
מצב הדגמה ללוח יד 2, בלי שרת: `http://localhost:5173/?demo=sale` (ליציאה: `?demo=off`).

## פריסה

### אתר (Firebase Hosting)

```bash
cd client
npm run build
npx firebase deploy --only hosting --project rent-photo
```

### שרת (Cloud Run)

```bash
cd server
gcloud builds submit . --tag me-west1-docker.pkg.dev/rent-photo/cloud-run-source-deploy/photorental-api:<tag> --project rent-photo
gcloud run services update photorental-api --image me-west1-docker.pkg.dev/rent-photo/cloud-run-source-deploy/photorental-api:<tag> --region me-west1 --project rent-photo
```

אפשר גם להריץ את `server/deploy/deploy-backend.sh` מתוך Google Cloud Shell.
הסקריפטים שבתיקייה `server/deploy` יוצרים את הסודות ב-Secret Manager ומחברים אותם לשרת.

## סודות והגדרות

**אף סוד לא נשמר בקוד.** בסביבת הייצור כל הסודות נשמרים ב-Google Secret Manager, ומחוברים לשרת כמשתני סביבה:

| משתנה | תפקיד |
|---|---|
| `Jwt__Key` | חתימת טוקני ההתחברות |
| `Encryption__PayoutKey` | הצפנת פרטי חשבון הבנק של המשכירות |
| `Scheduler__Key` | אימות הקריאות של Cloud Scheduler |
| `Resend__ApiKey` | שליחת מיילים |
| `Twilio__AuthToken` | שיחות טלפון ואימות חתימות של Twilio |
| `Summit__*` | סליקת אשראי |
| `GetPackage__ApiKey` | הזמנת שליחים |
| `Commission__BankDetails` | פרטי חשבון הבנק לתשלום דוח העמלות |

קבצים שאסור להעלות לגיט, ולכן הם מוחרגים ב-`.gitignore`:
- `server/PhotoRental.API/firebase-key.json`
- `server/PhotoRental.API/appsettings.Development.json`
- `client/.env`

## אבטחה בקצרה

- **התחברות:** בטוקן JWT. פעולות ניהול מוגבלות לתפקיד Admin.
- **קריאות מ-Twilio** (סטטוס שיחה, הקשות, שיחות נכנסות): מאומתות בחתימת Twilio.
- **קישורים במיילים** (אישור זמינות, "הפריט נאסף", חזרה לשיחה, דף רכישה): מוגנים בטוקן אקראי, שנבדק בהשוואה בזמן קבוע.
- **CORS:** השרת מקבל בקשות רק מהדומיין של האתר.
- **פרטי חשבון בנק:** מוצפנים בשרת, ומספרי הטלפון של הצדדים לא נחשפים זה לזה.

## זכויות

© COMMON. כל הזכויות שמורות. הקוד מפורסם לצפייה בלבד, ואין רשות להעתיק או להשתמש בו בלי אישור.
