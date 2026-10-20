# PhotoRental.Client

אתר הלקוח (React + Vite) של PhotoRental - אתר להשכרת ציוד צילום. מתחבר ל-PhotoRental.API
(פרויקט ה-ASP.NET Core שנמצא בתיקיות האחיות של הסולושן) ומציג קטלוג פריטים מחולק לקטגוריות,
מערכת הרשמה/התחברות, ואזור אישי (ההשכרות שלי, ולמשתמשי "משכיר" - ניהול הציוד שלי).

## הרצה מקומית

1. ודאו ש-PhotoRental.API רץ (למשל דרך Visual Studio, F5) - ברירת המחדל היא
   `http://localhost:5116`. אפשר לוודא זאת ע"י פתיחת `http://localhost:5116/swagger`.
2. התקינו את התלויות:

   ```bash
   npm install
   ```

3. הריצו את שרת הפיתוח:

   ```bash
   npm run dev
   ```

4. פתחו את הכתובת שתוצג (בדרך כלל `http://localhost:5173`).

## חיבור לשרת

כתובת ה-API מוגדרת בקובץ `src/config.js`. ברירת המחדל (`http://localhost:5116/api`) תואמת
לפרופיל ה-HTTP ב-launchSettings.json של PhotoRental.API. אם אתם מריצים את השרת עם הפרופיל
HTTPS, שנו שם ל-`https://localhost:7244/api`.

## התחברות והרשמה

הרשמה מתבצעת מול `/api/auth/register` והתחברות מול `/api/auth/login` - שני אלה מחזירים טוקן
JWT שנשמר בדפדפן (localStorage) ומצורף אוטומטית לכל בקשה הבאה לשרת. בעת ההרשמה בוחרים אם
אתם "שוכרים" ציוד או "משכירים" ציוד - משכירים רואים באזור האישי גם ניהול ציוד משלהם.

## בנייה לפריסה

```bash
npm run build
```

התוצר ייכתב לתיקיית `dist/`.

## פריסה ל-Firebase Hosting (https://commonrent.web.app)

התשתית מוכנה ופועלת: `firebase.json` מצביע ל-site בשם `commonrent` תחת פרויקט `rent-photo`
(מוגדר גם ב-`.firebaserc`). `.env.production` מגדיר `VITE_API_BASE_URL` ככתובת המלאה של שירות
ה-Cloud Run (`https://photorental-api-826241029359.me-west1.run.app/api`) - קריאה חוצת-דומיינים
רגילה (cross-origin), לא rewrite - כך שאין תלות בין הפריסה של האתר לפריסה של השרת.

לפריסה:

```bash
npm run deploy
```

(זה מריץ `vite build` ואז `firebase deploy --only hosting` דרך `npx`, כך שאין צורך בהתקנה גלובלית
של firebase-tools).

**חשוב**: אם מעדכנים את `PhotoRental.API/appsettings.Production.json` (כתובת השרת/CORS) - צריך
גם לפרוס מחדש את השרת עצמו כדי שהשינוי ייכנס לתוקף:

```bash
gcloud run deploy photorental-api --region europe-west1 --project rent-photo --allow-unauthenticated --set-secrets="/secrets/firebase-key.json=firebase-service-account:latest" --set-env-vars="Firebase__CredentialsPath=/secrets/firebase-key.json" --source .
```

(מריצים מתיקיית השורש של הפתרון, `PhotoRental/`. פקודת `gcloud` דורשת רשת ללא NetFree כדי לעבוד -
ראו הערה למטה).

**הערת NetFree**: ברשתות עם פילטר NetFree, גם ה-CLI של gcloud (מבוסס Python) וגם דומיינים חדשים/לא
מוכרים (כמו `*.web.app` ו-`*.run.app`) עלולים להיחסם עד לאישור/סיווג. אם אתר או שרת חדש מציג
"Blocked by NetFree" או "האתר עדיין לא נבדק" - יש קישור באתר החסימה עצמו לשליחת בקשת בדיקה ל-NetFree.


## בדיקת דוח העמלה בסביבת פיתוח

> סביבת הפיתוח מתחברת למסד הנתונים האמיתי (Firebase `rent-photo`) - להשתמש בחשבונות בדיקה ולמחוק אותם אחר כך.

1. להריץ: `dotnet run --launch-profile http` (בתיקיית `PhotoRental.API`) ו-`npm run dev` (בתיקיית `PhotoRental.Client`).
2. להירשם בשני חשבונות (משכירה ושוכרת). המשכירה מוסיפה פריט ומאשרת את תנאי העמלה (7%).
3. השוכרת שוכרת את הפריט במזומן ואז לוחצת "סימון כהוחזר" באזור האישי.
4. אצל המשכירה, באזור האישי, מופיע כרטיס "עמלת שירות" עם הסכום והפירוט.
5. לראות את המייל: ב-Firestore, באוסף `Users`, להגדיר `Role` = `Admin`, להתחבר מחדש, ואז "ניהול האתר" ← "עמלות" ← "תצוגה מקדימה".
6. שליחה אמיתית דורשת מפתח Resend: `dotnet user-secrets set "Resend:ApiKey" "<המפתח>"` (בתיקיית `PhotoRental.API`). בלעדיו כפתור "שליחה במייל" מחזיר הודעה שהשירות לא מוגדר ולא מסמן את הדוח כנשלח.
