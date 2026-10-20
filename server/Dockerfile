# בונה את PhotoRental.API כתמונת Docker אחת (multi-stage) להרצה ב-Google Cloud Run.
# שלב הבנייה משתמש ב-SDK המלא (כבד יותר), ושלב ההרצה מעתיק רק את התוצר הקומפל לתמונת runtime
# קלה בהרבה - כדי שהתמונה הסופית שעולה ל-Cloud Run תהיה קטנה ותעלה מהר.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# מעתיקים קודם רק את קבצי ה-csproj (לא את כל הקוד) כדי ש-dotnet restore ירוץ מחדש רק כשתלויות
# משתנות, ולא בכל שינוי קוד - מאיץ בנייות חוזרות הודות למנגנון ה-cache של Docker.
COPY PhotoRental.sln .
COPY PhotoRental.API/PhotoRental.API.csproj PhotoRental.API/
COPY PhotoRental.Core/PhotoRental.Core.csproj PhotoRental.Core/
COPY PhotoRental.Data/PhotoRental.Data.csproj PhotoRental.Data/
COPY PhotoRental.Service/PhotoRental.Service.csproj PhotoRental.Service/
RUN dotnet restore PhotoRental.API/PhotoRental.API.csproj

COPY PhotoRental.API/ PhotoRental.API/
COPY PhotoRental.Core/ PhotoRental.Core/
COPY PhotoRental.Data/ PhotoRental.Data/
COPY PhotoRental.Service/ PhotoRental.Service/
RUN dotnet publish PhotoRental.API/PhotoRental.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Cloud Run מצפה שהקונטיינר יאזין לפורט 8080 כברירת מחדל.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "PhotoRental.API.dll"]
