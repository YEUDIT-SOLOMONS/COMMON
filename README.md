# PhotoRental – פרויקט סיום .NET

שרת ASP.NET Core Web API (‏.NET 9) לקהילת השכרת ציוד צילום. משכירות מעלות ציוד (מצלמות, עדשות, תאורה,
סטודיו), שוכרות מזמינות אותו לתאריכים מסוימים, והמערכת מנהלת לוח זמנים, תשלום, משלוח והחזרה.

## מבנה ה-repository

```
server/   השרת – ASP.NET Core Web API (‏PhotoRental.sln, ארבע שכבות ופרויקט בדיקות)
client/   הלקוח – React + Vite, להדגמת השרת
```

## המשאב המוגבל והתחרות עליו

**המשאב:** פריט ציוד (`Item`) עם מספר יחידות מוגבל (`Quantity`, לרוב 1) בטווח תאריכים.
**התחרות:** שתי שוכרות לוחצות "השכירי" על אותה מצלמה לאותם תאריכים באותו רגע. שתיהן עוברות את
בדיקת הזמינות (`ItemScheduleService.EnsureFreeAsync`) כי אף אחת עוד לא שמרה.

**הפתרון (optimistic concurrency):**
1. כשהפריט נקרא, נשמר גם ה-`updateTime` שלו ב-Firestore. זה ה-concurrency token (`Item.ConcurrencyToken`),
   המקבילה של `rowversion`/`xmin`. לפריט יש גם מונה `Version` שעולה בכל השכרה.
2. השמירה (`LoanRepository.CreateWithItemCheckAsync`) היא פעולת `commit` אטומית אחת שכוללת יצירת
   ההשכרה **וגם** העלאת `Version` של הפריט, עם תנאי מוקדם `currentDocument.updateTime` = הערך שנקרא.
   זו המקבילה של הוספת ה-token לתנאי ה-`WHERE`.
3. אם מישהי השכירה בינתיים, ה-`updateTime` כבר השתנה, Firestore דוחה את כל ה-commit ושום דבר לא נשמר.
   השכבה זורקת `ConcurrencyConflictException` (המקבילה של `DbUpdateConcurrencyException`).
4. `LoanService` תופס את החריגה הספציפית הזו בלבד, וה-middleware מחזיר **409 Conflict** עם ההודעה
   "הפריט נתפס על ידי משתמשת אחרת ממש עכשיו. נסי שוב או בחרי תאריכים אחרים." ורושם לוג ברמת Warning.

בדיקת הזמינות והשמירה קשורות זו לזו דרך ה-token: הבדיקה מתבססת על הגרסה שנקראה, והשמירה מצליחה רק
אם זו עדיין הגרסה הנוכחית. גם הזמנה של כמה פריטים יחד נשמרת ב-commit אחד: כולם או אף אחד.

**ההוכחה:** `PhotoRental.Tests/Data/FirestoreConcurrencyTests.cs`. שני מופעים נפרדים של
`FirebaseDbContext` (המקבילה של שני DbContext) קוראים את אותו פריט ומנסים להשכיר אותו. הראשון מצליח
והשני מקבל `ConcurrencyConflictException`. רצה מול Firestore Emulator (ראו "בדיקות").

## Firebase במקום EF Core

המרצה אישרה עבודה עם Firebase. כך מומשה כל דרישה שנכתבה ל-EF Core:

| דרישה | המימוש כאן |
|---|---|
| DbContext | `PhotoRental.Data/FirebaseDbContext.cs`: עטיפה ל-Firestore REST API, רשומה ב-DI |
| Migrations (לפחות שתיים) | `PhotoRental.Data/Migrations/DataMigrations.cs`: מחלקות `IDataMigration` ממוספרות (`0001_BackfillItemQueryFields`, `0002_AddItemVersion`) שרצות בעליית השרת. מה שרץ נרשם באוסף `_migrations`, בדומה ל-`__EFMigrationsHistory` |
| Seed data | `CategorySeeder` + `DemoDataSeeder`: קטגוריות, משתמשת לכל role ו-14 פריטים (בפיתוח בלבד, `Seed:DemoData`) |
| Connection string מהקונפיגורציה | `Firebase:ProjectId`, `Firebase:EmulatorHost`, `Firebase:CredentialsPath` מ-appsettings / User Secrets |
| Pagination (Skip/Take בשאילתה) | `GET /api/items/page`: ‏`offset` ו-`limit` נשלחים בתוך ה-`runQuery` של Firestore, והספירה ב-`runAggregationQuery` (`ItemRepository.GetPageAsync`) |
| AsNoTracking | לא רלוונטי: ל-Firestore אין change tracking, כל קריאה היא כבר "no tracking" |
| one-to-many | פריט ← השכרות (`Loan.ItemCopyId`), משכירה ← פריטים (`Item.OwnerId`) |
| many-to-many | משתמשת ↔ פריט דרך אוסף הקישור `Favorites` (מזהה `{UserId}_{ItemId}`) |
| Include / מניעת N+1 | אין joins ב-Firestore. אוספים את המזהים הקשורים ומביאים אותם בבקשה אחת (`FirebaseDbContext.BatchGetAsync`, `IItemRepository.GetByIdsAsync`), לא בלולאה |
| Fluent API | לא רלוונטי: אין סכמה. המיפוי בין ישות למסמך מרוכז ב-`ToFields`/`ToEntity` בכל Repository |
| rowversion / xmin | `updateTime` של המסמך + שדה `Version` (ראו למעלה) |

## ארכיטקטורה

```
PhotoRental.sln
  PhotoRental.Core      // Entities, Interfaces, DTOs, Enums, Exceptions, Options - בלי תלויות חיצוניות
  PhotoRental.Data      // FirebaseDbContext, Repositories, Migrations
  PhotoRental.Service   // לוגיקה עסקית, AutoMapper Profiles, Seeders
  PhotoRental.API       // Controllers, Middleware, Program.cs, nlog.config
  PhotoRental.Tests     // xUnit + Moq, בדיקות pipeline, בדיקות מול האמולטור
```

- **Middleware:** ‏`GlobalExceptionMiddleware` (ראשון בצנרת, תשובת JSON אחידה: `statusCode`, `message`,
  `correlationId`) ו-`CorrelationIdMiddleware` (מזהה לכל בקשה, כותרת `X-Correlation-Id`, לוג לכל בקשה).
  ‏Authentication לפני Authorization.
- **סטטוסים:** ‏409 להתנגשות, 503 לתקלה בבסיס הנתונים, 400 לולידציה (Data Annotations + תשובת ModelState
  אחידה), 404, 401, 403, ו-201 עם Location ביצירה.
- **לוגים:** ‏NLog (`nlog.config`), קבצים ב-`logs/` עם CorrelationId בכל שורה. הקוד משתמש ב-`ILogger` הרגיל.
  לא נרשמים סיסמאות, טוקנים או תוכן בקשות הרשמה.
- **JWT:** ‏`JwtOptions` דרך `IOptions`. שלושה roles: ‏`Admin`, ‏`Owner`, ‏`Renter`.
- **CancellationToken:** עובר מה-Controller ועד ה-HTTP ל-Firestore בשרשרת ההשכרה (`POST /api/loans`,
  `POST /api/loans/batch`) ובקריאת פריטים (`GET /api/items/page`, `GET /api/items/{id}`).

## הרצה מקומית

**דרוש:** ‏.NET 9 SDK, ו-Firestore Emulator (Java 21 ומעלה + Firebase CLI).

```bash
npm install -g firebase-tools
cd server
firebase emulators:start --only firestore --project photorental-dev
```

הגדרות האמולטור (פורט 8080) נמצאות בקובץ `server/firebase.json`, ולכן הפקודה עובדת מכאן בלי `firebase init`.

בחלון נפרד, מתיקיית `server`:

```bash
cd server
dotnet run --project PhotoRental.API --launch-profile http
```

- Swagger: http://localhost:5116/swagger
- בסביבת Development השרת מתחבר לאמולטור (`localhost:8080`), מריץ את ה-migrations וזורע נתוני דמו.
  אין צורך בשום פעולה ידנית בבסיס הנתונים.
- מפתח JWT: אם לא הוגדר ב-User Secrets, נוצר מפתח זמני לפיתוח. להגדרה קבועה:

```bash
dotnet user-secrets set "Jwt:Key" "<מפתח אקראי באורך 64 תווים לפחות>" --project PhotoRental.API
```

### לקוח

בחלון נוסף, כשהשרת רץ:

```bash
cd client
npm install
npm run dev
```

האתר עולה בכתובת http://localhost:5173 ומתחבר לשרת המקומי.

## משתמשות דמו

הסיסמה של כולן: `Demo1234`

| Role | אימייל |
|---|---|
| Admin | admin@demo.local |
| Owner (משכירה, בעלת פריטי הדמו) | owner@demo.local |
| Renter | renter@demo.local |
| Renter (לניסוי התחרות) | renter2@demo.local |

**לנסות את ה-409 ידנית:** מתחברים כ-renter ב-Swagger ובחלון נוסף כ-renter2, ושולחים כמעט יחד
`POST /api/loans` לאותו פריט ולאותם תאריכים. אחת מקבלת 201, השנייה 409 (או 409 של "כבר מושכר" אם הגיעה
אחרי שהראשונה כבר נשמרה).

## בדיקות

מתיקיית `server`:

```bash
dotnet test
```

- בדיקות יחידה על שכבת ה-Service עם Moq, כולל המקרה התקין והמקרה הנדחה של המשאב המוגבל
  (`LoanServiceTests`, `ItemScheduleServiceTests`) ובדיקת תקינות של ה-AutoMapper Profile.
- בדיקות pipeline עם `WebApplicationFactory`: ‏CorrelationId, ‏401, תשובת שגיאה אחידה ו-400 של ולידציה.
- בדיקות מול האמולטור (תחרות בין שני DbContext, דפדוף, migrations) רצות רק כשמוגדר
  `FIRESTORE_EMULATOR_HOST`, ואחרת מסומנות Skipped:

```bash
firebase emulators:exec --only firestore --project photorental-test "dotnet test"
```

## הערות

- **AutoMapper 15:** בהרצה מקומית נרשמת בלוג אזהרה על מפתח רישיון. לפי היצרן, שימוש בלי רישיון מותר בפיתוח ובבדיקות. אם יש מפתח, הוא נטען מ-`AutoMapper:LicenseKey` (User Secrets).
- **בדיקות:** הבדיקות שמול Firestore רצות בזו אחר זו, כי כולן חולקות את אותו אמולטור.
