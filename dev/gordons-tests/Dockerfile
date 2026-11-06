FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY OpenRestoApi/OpenRestoApi.csproj OpenRestoApi/
COPY OpenRestoApi.Core/OpenRestoApi.Core.csproj OpenRestoApi.Core/
COPY OpenRestoApi.Domain/OpenRestoApi.Domain.csproj OpenRestoApi.Domain/
COPY OpenRestoApi.Infrastructure/OpenRestoApi.Infrastructure.csproj OpenRestoApi.Infrastructure/
COPY OpenRestoApi.Tests/OpenRestoApi.Tests.csproj OpenRestoApi.Tests/
RUN dotnet restore OpenRestoApi.Tests/OpenRestoApi.Tests.csproj
COPY OpenRestoApi/ OpenRestoApi/
COPY OpenRestoApi.Core/ OpenRestoApi.Core/
COPY OpenRestoApi.Domain/ OpenRestoApi.Domain/
COPY OpenRestoApi.Infrastructure/ OpenRestoApi.Infrastructure/
COPY OpenRestoApi.Tests/ OpenRestoApi.Tests/
RUN dotnet build OpenRestoApi.Tests/OpenRestoApi.Tests.csproj --configuration Release --no-restore --verbosity quiet
ENTRYPOINT ["dotnet", "test", "OpenRestoApi.Tests/OpenRestoApi.Tests.csproj", "--configuration", "Release", "--no-build", "--no-restore", "--verbosity", "minimal", "-p:CollectCoverage=false"]
