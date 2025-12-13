# Root Dockerfile (for API)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and ALL project files
COPY TMS.sln .
COPY TMS.API/TMS.API.csproj ./TMS.API/
COPY TMS.Shared/TMS.Shared.csproj ./TMS.Shared/
COPY TMS.Web/TMS.Web.csproj ./TMS.Web/

# Restore dependencies
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet build -c Release --no-restore

# Publish API
WORKDIR /src/TMS.API
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "TMS.API.dll"]