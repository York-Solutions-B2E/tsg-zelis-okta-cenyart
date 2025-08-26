```zsh
// Projects
dotnet new webapi -n Api --framework net8.0
dotnet new blazor -n Blazor --framework net8.0
dotnet new classlib -n Shared --framework net8.0
dotnet new nunit -n Tests --framework net8.0

// References
dotnet add Api reference Shared
dotnet add Blazor reference Shared
dotnet add Tests reference Api
dotnet add Tests reference Blazor
dotnet add Tests reference Shared

// Solution
dotnet new sln
dotnet sln add Api
dotnet sln add Blazor
dotnet sln add Shared
dotnet sln add Tests
```

```zsh
// Blazor
OktaDomain
ClientId
ClientSecret
WebApiEndpointHttps

// Api
sqldata
OktaDomain
Audience
ClientId

// Public
Authority = OktaDomain = integrator-7281285.okta.com
Audience = api://default

// Private
dotnet user-secrets set "Okta:ClientId" ""
dotnet user-secrets set "Okta:ClientSecret" ""
dotnet user-secrets set "ConnectionStrings:ZelisOkta" "
    Server=localhost,1433;
    Database=ZelisOkta;
    User Id=sa;
    Password=YourStrongPassword123!;
    TrustServerCertificate=true"

// share secret id in .csproj
<UserSecretsId>guid</UserSecretsId>
dotnet user-secrets list --project Blazor
dotnet user-secrets list --project Api
```

```zsh
// Api
dotnet add package Microsoft.EntityFrameworkCore --project Api

// Tests
dotnet add package FluentAssertions --project Tests
dotnet add Tests/WebApi.Tests package Microsoft.EntityFrameworkCore.InMemory --project Tests
```

```bash
docker run -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=YourStrongPassword123!' \
  -p 1433:1433 --name ZelisOktaLocal \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

```zsh
dotnet clean
dotnet build
dotnet test
dotnet run
```