# Loan API
ASP.NET Core 8 Web API მომხმარებლების რეგისტრაციისთვის, JWT ავთენტიფიკაციისთვის, სესხების მართვისთვის, ბუღალტრის მიერ სესხების განხილვისთვის, მომხმარებლის დროებითი დაბლოკვისთვის, მონაცემების ვალიდაციისთვის, შეცდომების დამუშავებისა და აუდიტის ლოგირებისთვის.

## ტექნოლოგიები

* ASP.NET Core 8 და C#
* Entity Framework Core და Microsoft SQL Server 2022
* JWT Bearer Authentication და როლებზე დაფუძნებული ავტორიზაცია
* BCrypt პაროლების ჰეშირებისთვის
* FluentValidation მოთხოვნების ვალიდაციისთვის
* Serilog კონსოლსა და ფაილებში ლოგირებისთვის
* SQL-ზე დაფუძნებული API აუდიტის ლოგები
* Swagger/OpenAPI
* xUnit ინტეგრაციული და Unit ტესტებისთვის

## მოთხოვნები

* .NET 8 SDK ან უფრო ახალი SDK, რომელსაც შეუძლია .NET 8-ზე მუშაობა
* SQL Server 2022 (მხარდაჭერილია Express ვერსიაც)
* სურვილისამებრ: SQL Server Management Studio

## კონფიგურაცია

Default connection `localhost\SQLEXPRESS`, მონაცემთა ბაზის სახელია `LoanApiDb`.

თუ თქვენს SQL Server-ს სხვა Instance სახელი აქვს, შეცვალეთ `LoanApi.Api/appsettings.json` ფაილში:

`ConnectionStrings:DefaultConnection`

JWT-ის გასაღები კოდში არ ინახება. ლოკალურად მუშაობისთვის უნდა დავამატოთ მინიმუმ 32 სიმბოლოსგან შემდგარი საიდუმლო გასაღები:

dotnet user-secrets set "Jwt:Key" "your-secret-key-at-least-32-characters" --project LoanApi.Api

Deployment-ის დროს გასაღები უნდა იყოს მითითებული Jwt__Key Environment Variable-ად.

## მონაცემთა ბაზის გამართვა

მონაცემთა ბაზის შექმნა ან განახლება შესაძლებელია არსებულ EF Core Migration-ების საფუძველზე:

```powershell
dotnet ef database update --project LoanApi.Api --startup-project LoanApi.Api
```

აპლიკაცია ასევე ავტომატურად იყენებს SQL Server Migration-ებს გაშვებისას.

მონაცემთა ბაზის სტრუქტურაში ცალკე ცხრილებია:

* `Users`
* `Accountants`
* `Loans`
* `AuditLogs`

`Loans.UserId` დაკავშირებულია `Users.Id`-თან.

აპლიკაციის გაშვებისას წინასწარ განსაზღვრული მომხმარებლები ან სესხები ავტომატურად არ ემატება.

## აპლიკაციის გაშვება

```powershell
dotnet run --project LoanApi.Api
```

Swagger-ის გახსნა შესაძლებელია:

`http://localhost:5117/swagger`

Root URL ავტომატურად გადამისამართდება Swagger-ზე.

## ავთენტიფიკაცია

მომხმარებლის რეგისტრაცია შესაძლებელია:

`POST /api/auth/register`

ხოლო ავტორიზაცია:

`POST /api/auth/login`

წარმატებული მოთხოვნის შემთხვევაში ბრუნდება 2-საათიანი JWT Token, რომელიც შეიცავს მომხმარებლის ID-ს, Username-ს და როლს — `User` ან `Accountant`.

პაროლები ინახება მხოლოდ BCrypt-ის საშუალებით ჰეშირებულად.

Swagger-ში ავტორიზაციისთვის აირჩიეთ **Authorize** და შეიყვანეთ მიღებული Token.

სხვა API Client-ის გამოყენებისას მოთხოვნას უნდა დაემატოს:

`Authorization: Bearer <token>`

### რეგისტრაციის მაგალითი

```json
{
  "firstName": "Nino",
  "lastName": "Beridze",
  "username": "nino.user",
  "age": 29,
  "email": "nino@example.com",
  "monthlyIncome": 4200,
  "password": "Password123!"
}
```

### Login-ის მაგალითი

```json
{
  "username": "nino.user",
  "password": "Password123!"
}
```

საჯარო რეგისტრაციისას ყოველთვის იქმნება `User`. `Accountant` მომხმარებლის შექმნა ხდება მხოლოდ ადმინისტრაციული გზით, რათა მომხმარებელმა თავად ვერ მიანიჭოს საკუთარ თავს Accountant-ის როლი.

## API Endpoints

### Authentication

| მეთოდი | Endpoint             | წვდომა    | დანიშნულება                                                  | წარმატება |
| ------ | -------------------- | --------- | ------------------------------------------------------------ | --------- |
| POST   | `/api/auth/register` | Anonymous | მომხმარებლის რეგისტრაცია და JWT-ის დაბრუნება                 | 201       |
| POST   | `/api/auth/login`    | Anonymous | მომხმარებლის ან ბუღალტრის ავთენტიფიკაცია და JWT-ის დაბრუნება | 200       |

### Users

| მეთოდი | Endpoint                | წვდომა             | დანიშნულება                               | წარმატება |
| ------ | ------------------------ | ------------------- | ----------------------------------------- | --------- |
| GET    | `/api/user/{id}`         | Owner ან Accountant | მომხმარებლის პროფილის მიღება             | 200       |
| PUT    | `/api/user/{id}/block`   | Accountant          | მომხმარებლის მიერ სესხის შექმნის დაბლოკვა | 204       |
| PUT    | `/api/user/{id}/unblock` | Accountant          | დაბლოკვის მოხსნა                          | 204       |

განუსაზღვრელი ვადით დაბლოკვისთვის გამოიყენება:

```json
{
  "blockedUntil": null
}
```

დროებითი დაბლოკვისთვის საჭიროა მომავლის UTC დრო, მაგალითად:

```json
{
  "blockedUntil": "2026-09-01T12:00:00Z"
}
```

### მომხმარებლის სესხები

ეს Endpoints ხელმისაწვდომია მხოლოდ `User` როლისთვის.

მომხმარებელს შეუძლია მხოლოდ საკუთარი სესხების ნახვა და შეცვლა.

სესხის განახლება და წაშლა შესაძლებელია მხოლოდ მაშინ, როდესაც მისი სტატუსია `Processing`.

| მეთოდი | Endpoint          | დანიშნულება                                   | წარმატება |
| ------ | ----------------- | --------------------------------------------- | --------- |
| GET    | `/api/loans`      | ავტორიზებული მომხმარებლის ყველა სესხის მიღება | 200       |
| GET    | `/api/loans/{id}` | მომხმარებლის კონკრეტული სესხის მიღება         | 200       |
| POST   | `/api/loans`      | ახალი სესხის შექმნა `Processing` სტატუსით     | 201       |
| PUT    | `/api/loans/{id}` | საკუთარი `Processing` სესხის განახლება        | 204       |
| DELETE | `/api/loans/{id}` | საკუთარი `Processing` სესხის წაშლა            | 204       |

### სესხის შექმნის/განახლების მაგალითი

```json
{
  "loanType": "FastLoan",
  "amount": 1500,
  "currency": "GEL",
  "period": 12
}
```

სესხის ტიპებია:

* `FastLoan`
* `AutoLoan`
* `Installment`

სესხის სტატუსებია:

* `Processing`
* `Approved`
* `Rejected`

ვალუტა უნდა შედგებოდეს 3 სიმბოლოსგან, თანხა უნდა იყოს დადებითი, ხოლო პერიოდი უნდა იყოს 1-დან 360 თვემდე.

## Accountant — სესხები და ლოგები

| მეთოდი | Endpoint                     | დანიშნულება                                   | წარმატება |
| ------ | ---------------------------- | --------------------------------------------- | --------- |
| GET    | `/api/accountant/loans`      | ყველა მომხმარებლის სესხების მიღება            | 200       |
| GET    | `/api/accountant/loans/{id}` | ნებისმიერი სესხის მიღება                      | 200       |
| PUT    | `/api/accountant/loans/{id}` | ნებისმიერი სესხისა და მისი სტატუსის განახლება | 204       |
| DELETE | `/api/accountant/loans/{id}` | ნებისმიერი სესხის წაშლა სტატუსის მიუხედავად   | 204       |
| GET    | `/api/accountant/audit-logs` | ბოლო 100 API Audit Log-ის მიღება              | 200       |

### Accountant-ის მიერ სესხის განახლების მაგალითი

```json
{
  "loanType": "AutoLoan",
  "amount": 35000,
  "currency": "GEL",
  "period": 60,
  "status": "Approved"
}
```

## Response და Error სტატუსები

* **200 OK** — მოთხოვნა წარმატებით შესრულდა
* **201 Created** — რესურსი წარმატებით შეიქმნა
* **204 No Content** — მოთხოვნა წარმატებით შესრულდა, საპასუხო მონაცემი არ ბრუნდება
* **400 Bad Request** — მოთხოვნა არასწორია ან ვერ გაიარა ვალიდაცია
* **401 Unauthorized** — ავთენტიფიკაცია არ არის გავლილი, Token ვადაგასულია ან არასწორია
* **403 Forbidden** — მომხმარებელს არ აქვს შესაბამისი როლი, მომხმარებელი დაბლოკილია ან ცდილობს სხვა მომხმარებლის რესურსზე წვდომას
* **404 Not Found** — მომხმარებელი ან სესხი ვერ მოიძებნა
* **409 Conflict** — რეგისტრაციისას დაფიქსირდა დუბლირებული მონაცემები ან იცვლება ისეთი სესხი, რომლის სტატუსიც `Processing` არ არის
* **500 Internal Server Error** — სერვერზე მოხდა გაუთვალისწინებელი შეცდომა

დამუშავებული შეცდომების შემთხვევაში ბრუნდება:

```json
{
  "message": "..."
}
```

ვალიდაციის შეცდომები ბრუნდება Validation Problem Details-ის ფორმატში.

უსაფრთხოების მიზნით Stack Trace API Response-ში არასდროს ბრუნდება.

## Logging

Serilog ლოგებს წერს როგორც კონსოლში, ასევე ყოველდღიურ ფაილებში:

`LoanApi.Api/logs`

API-ის აქტივობები ასევე ინახება `AuditLogs` ცხრილში.

აუდიტის ლოგში ინახება:

* HTTP Method
* Request Path
* მომხმარებლის იდენტიფიკაცია
* Response Status
* Request Duration
* IP Address
* User Agent
* UTC Timestamp

Request Body და პაროლები Audit Log-ში არ ინახება.

## Tests

ტესტებისთვის საჭიროა ცალკე SQL Server მონაცემთა ბაზის ერთხელ კონფიგურაცია.

მონაცემთა ბაზის სახელი აუცილებლად უნდა მთავრდებოდეს `_Tests`-ით. ეს უსაფრთხოების მექანიზმი უზრუნველყოფს, რომ ტესტებმა აპლიკაციის ძირითად მონაცემთა ბაზაზე არ იმუშაონ.

```powershell
dotnet user-secrets set "ConnectionStrings:TestConnection" "Server=localhost\SQLEXPRESS;Database=LoanApiDb_Tests;Trusted_Connection=True;TrustServerCertificate=True" --project LoanApi.Tests
```

SQL Server Fixture ავტომატურად ქმნის მონაცემთა ბაზას, იყენებს რეალურ Migration-ებს და თითოეული MSSQL ტესტის წინ და შემდეგ ასუფთავებს სატესტო მონაცემებს.

ტესტების დროს მონაცემთა ბაზა არ იშლება.

ყველა ტესტის გასაშვებად:

```powershell
dotnet test LoanApi.sln
```

ტესტების პროექტი მოიცავს:

* Unit Tests
* SQLite HTTP Integration Tests
* რეალურ SQL Server-ზე Integration Tests
* Migration-ების შემოწმებას
* მონაცემთა ბაზის Schema-ს ტიპების შემოწმებას
* Default Values-ის შემოწმებას
* Unique Index-ების შემოწმებას
* Check Constraints-ის შემოწმებას
* Cascade Delete-ის შემოწმებას
* მონაცემების შენახვისა და Persistence-ის შემოწმებას

მხოლოდ SQL Server ტესტების გასაშვებად:

```powershell
dotnet test LoanApi.Tests --filter "FullyQualifiedName~SqlServerIntegrationTests"
```
