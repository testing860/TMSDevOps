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

# Install sqlcmd and dependencies
RUN apt-get update -y && \
    apt-get install -y curl gnupg apt-transport-https && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/ubuntu/22.04/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update -y && \
    ACCEPT_EULA=Y apt-get install -y msodbcsql18 mssql-tools unixodbc-dev && \
    echo 'export PATH="$PATH:/opt/mssql-tools/bin"' >> /etc/bash.bashrc

# Copy published API
COPY --from=build /app/publish .

EXPOSE 5000

# Use existing entrypoint
ENTRYPOINT ["dotnet", "TMS.API.dll"]

