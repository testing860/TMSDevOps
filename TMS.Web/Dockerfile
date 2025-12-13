# TMS.Web Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Stage 1: Build everything
WORKDIR /src

# Copy ALL project files
COPY TMS.sln .
COPY TMS.API/TMS.API.csproj TMS.API/
COPY TMS.Web/TMS.Web.csproj TMS.Web/
COPY TMS.Shared/TMS.Shared.csproj TMS.Shared/

# Restore all projects
RUN dotnet restore

# Copy ALL source code
COPY TMS.API/ TMS.API/
COPY TMS.Shared/ TMS.Shared/
COPY TMS.Web/ TMS.Web/

# Build and publish the web project
WORKDIR /src/TMS.Web
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Web runtime
FROM nginx:alpine
WORKDIR /usr/share/nginx/html

# Copy published files
COPY --from=build /app/publish/wwwroot/ .

# Copy nginx config (relative to build context root)
COPY TMS.Web/nginx.conf /etc/nginx/nginx.conf

# Test nginx config and show any errors
RUN echo "=== Testing nginx config ===" && \
    nginx -t 2>&1 || echo "Nginx test failed" && \
    echo "=== Nginx config content ===" && \
    cat /etc/nginx/nginx.conf | head -20

# List files to verify everything is there
RUN echo "=== Web directory contents ===" && \
    ls -la && \
    echo "=== Checking for CSS files ===" && \
    find . -name "*.css" | head -5

EXPOSE 80