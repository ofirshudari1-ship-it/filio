# RELEASE-CHECKLIST.md — Filio

בצע לפני כל שחרור גרסה:

## קוד

- [ ] כל הבדיקות עוברות: `dotnet test app/Filio.Tests`
- [ ] גרסה עודכנה ב-`version.json`, `build/installer/setup.iss`, `Filio.App.csproj`
- [ ] `CHANGELOG.md` עודכן בפורמט Keep a Changelog
- [ ] אין secrets בקוד (`git grep -i "password\|apikey\|secret"`)
- [ ] build נקי: `dotnet build app/Filio.sln -c Release`

## Installer

- [ ] `Filio-Setup-<version>.exe` נבנה מחדש: `build.ps1`
- [ ] קובץ ה-exe בשורש הפרויקט (לא ב-`build/`)
- [ ] התקנה נקייה על מכונה נקייה עובדת
- [ ] עדכון מגרסה קודמת = רשומה אחת ב"הוספה והסרה"
- [ ] הסרה מנקה הכל; שאלת "מחק הגדרות?" עובדת
- [ ] מצב "בלי הרשאות אדמין" — נפילה חזרה ל-per-user לא כשל

## UI/UX

- [ ] Splash מוצג ונסגר לבד (1.5-2.5 שניות)
- [ ] Onboarding רץ פעם אחת בלבד, Skip עובד
- [ ] מתג שפה עברית↔אנגלית עובד, RTL מלא בעברית
- [ ] Dark Mode ו-Light Mode עובדים
- [ ] זיהוי ערכת נושא מהמערכת אוטומטי עובד
- [ ] כל כפתורי Save שומרים ומציגים confirmation

## שחרור

- [ ] git tag: `git tag v2.x.x && git push origin v2.x.x`
- [ ] `Filio-Setup-<version>.exe` → שורש הפרויקט (אין קובץ התקנה ישן/גרסה קודמת נשאר לצידו)
- [ ] `latest.json` עודכן בשרת העדכונים (אם קיים)
