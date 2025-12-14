pipeline {
    agent any

    environment {
        GITHUB_SSH_CREDENTIAL_ID = 'f2279fbb-b675-4191-bb6e-5e5c0d1421a5'
        DB_PASSWORD = credentials('tms-db-password')
        JWT_KEY = credentials('tms-jwt-key')
        ADMIN_PASSWORD = credentials('tms-admin-password')
        DEPLOY_PATH = '/opt/tms-app-docker'
    }

    stages {
        stage('Checkout from GitHub') {
            steps {
                checkout scm
            }
        }

        stage('Clean Repository Structure') {
            steps {
                sh '''
                    echo "🧹 Cleaning repository structure..."
                    if [ -d "TMS.Web/TMS.API" ]; then
                        echo "❌ Found incorrect TMS.API folder inside TMS.Web - removing..."
                        rm -rf TMS.Web/TMS.API
                    fi
                '''
            }
        }

        stage('Build & Test Application') {
            steps {
                sh '''
                    echo "Building .NET solution..."
                    dotnet build --configuration Release --verbosity minimal
                    echo "Running tests..."
                    dotnet test --configuration Release --logger trx --no-build
                '''
            }
        }

        stage('Prepare Environment') {
            steps {
                sh '''
                    echo "🔧 Creating .env file..."
                    
                    # Store credentials in temporary variables (safe single-quote handling)
                    DB_PASS='"'"${DB_PASSWORD}"'"'
                    JWT_KEY_VAL='"'"${JWT_KEY}"'"'
                    ADMIN_PASS='"'"${ADMIN_PASSWORD}"'"'
                    
                    cat > .env << EOF
DB_SERVER=sql-server
DB_NAME=TaskManagementSystem
DB_USER=sa
DB_PASSWORD=${DB_PASS}

JWT_KEY=${JWT_KEY_VAL}
JWT_ISSUER=TMSAPI
JWT_AUDIENCE=TMSWebClient

ADMIN_EMAIL=admin@tms.com
ADMIN_PASSWORD=${ADMIN_PASS}
ADMIN_DISPLAYNAME=Admin

ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
EOF
                    
                    chmod 600 .env
                    echo "✅ .env file created"
                '''
            }
        }

        stage('Deploy Locally') {
            steps {
                script {
                    sh '''#!/bin/bash
set -euo pipefail

echo "🚀 Starting local Docker deployment"
echo "Workspace: ${WORKSPACE}"
echo "Deploy path: ${DEPLOY_PATH}"

cd "${WORKSPACE}"

# --- Docker / Compose detection ---
if ! command -v docker >/dev/null 2>&1; then
    echo "❌ Docker is not installed"
    exit 1
fi

if command -v docker-compose >/dev/null 2>&1; then
    DC_CMD="docker-compose"
    echo "✅ Using docker-compose (v1)"
elif docker compose version >/dev/null 2>&1; then
    DC_CMD="docker compose"
    echo "✅ Using docker compose (v2)"
else
    echo "❌ Docker Compose not found"
    exit 1
fi

# --- Stop/cleanup old host services ---
echo "=== STEP 1: Stop old TMS services (if any) ==="
sudo systemctl stop tms-api.service 2>/dev/null || true
sudo systemctl stop tms-app.service 2>/dev/null || true
sudo systemctl disable tms-api.service 2>/dev/null || true
sudo systemctl disable tms-app.service 2>/dev/null || true

echo "=== STEP 2: Remove old nginx config (host) ==="
sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
sudo systemctl restart nginx 2>/dev/null || true

# --- Prepare deployment directory and .env ---
echo "=== STEP 3: Prepare deployment directory ==="
sudo mkdir -p "${DEPLOY_PATH}"
sudo cp -f .env "${DEPLOY_PATH}/" 2>/dev/null || true
sudo chmod 644 "${DEPLOY_PATH}/.env" 2>/dev/null || true
echo "✅ .env copied to ${DEPLOY_PATH} (chmod 644)"

echo "🔧 Ensuring SQL Server data directory exists and is writable..."
if [ ! -d "${DEPLOY_PATH}/sqlserver-data" ]; then
    sudo mkdir -p "${DEPLOY_PATH}/sqlserver-data"
fi
sudo chown -R 10001:10001 "${DEPLOY_PATH}/sqlserver-data" 2>/dev/null || true
sudo chmod -R 770 "${DEPLOY_PATH}/sqlserver-data" 2>/dev/null || true
echo "✅ sqlserver-data ready"

# --- Sanitize nginx config ---
echo "Sanitizing TMS.Web/nginx.conf (if needed)..."
if [ -f "TMS.Web/nginx.conf" ]; then
    if grep -q "sudo " TMS.Web/nginx.conf >/dev/null 2>&1 || ! grep -q "events {" TMS.Web/nginx.conf >/dev/null 2>&1; then
        awk "/events \\{/{p=1} p{print}" TMS.Web/nginx.conf > TMS.Web/nginx.conf.sanitized || true
        if [ -s TMS.Web/nginx.conf.sanitized ]; then
            mv TMS.Web/nginx.conf.sanitized TMS.Web/nginx.conf
            echo "✅ nginx.conf sanitized"
        else
            echo "❌ nginx.conf sanitization failed - leaving original"
        fi
    else
        echo "✅ nginx.conf looks OK"
    fi
else
    echo "⚠️ TMS.Web/nginx.conf not found"
fi

# --- Copy project files to deploy path ---
echo "Copying project files to ${DEPLOY_PATH}..."
sudo cp -f docker-compose.yml Dockerfile "${DEPLOY_PATH}/" 2>/dev/null || true
sudo cp -f TMS.Web/Dockerfile "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
sudo cp -f TMS.Web/nginx.conf "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
sudo rm -rf "${DEPLOY_PATH}/TMS.API" "${DEPLOY_PATH}/TMS.Web" "${DEPLOY_PATH}/TMS.Shared" 2>/dev/null || true
sudo cp -r TMS.API TMS.Web TMS.Shared "${DEPLOY_PATH}/" 2>/dev/null || true
sudo chown -R jenkins:jenkins "${DEPLOY_PATH}/" 2>/dev/null || true
echo "✅ Files copied to ${DEPLOY_PATH}"

cd "${DEPLOY_PATH}"

# --- Build Docker images ---
echo "=== STEP 4: Build Docker images ==="
${DC_CMD} down --remove-orphans 2>/dev/null || true
${DC_CMD} build --no-cache

# --- Start SQL Server first ---
echo "=== STEP 5: Start SQL Server container ==="
# First, ensure the data directory has proper permissions
sudo chown -R 10001:0 "${DEPLOY_PATH}/sqlserver-data"
sudo chmod -R 777 "${DEPLOY_PATH}/sqlserver-data"
${DC_CMD} up -d sql-server || true

# --- Wait for SQL Server to be ready ---
sql_is_ready() {
    if docker exec tms-sqlserver sh -c "command -v /opt/mssql-tools/bin/sqlcmd" >/dev/null 2>&1; then
        docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" >/dev/null 2>&1 && return 0 || return 1
    else
        docker run --rm --network container:tms-sqlserver mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" >/dev/null 2>&1 && return 0 || return 1
    fi
}

echo "Waiting for SQL Server to accept connections (timeout 180s)..."
counter=0
until sql_is_ready; do
    counter=$((counter+1))
    if [ ${counter} -ge 36 ]; then
        echo "❌ SQL Server did not become ready within timeout. Logs:"
        docker logs tms-sqlserver --tail=100 || true
        exit 1
    fi
    echo "⏳ waiting (${counter})..."
    sleep 5
done
echo "✅ SQL Server ready"

# --- Start API and Web ---
echo "Starting API and Web containers..."
${DC_CMD} up -d tms-api tms-web || true

# --- Initialize DB if missing ---
echo "=== STEP 6: Ensure TaskManagementSystem DB exists ==="
if docker exec tms-sqlserver sh -c "command -v /opt/mssql-tools/bin/sqlcmd" >/dev/null 2>&1; then
    docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TaskManagementSystem') CREATE DATABASE TaskManagementSystem;" || true
else
    docker run --rm --network container:tms-sqlserver mcr.microsoft.com/mssql-tools /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TaskManagementSystem') CREATE DATABASE TaskManagementSystem;" || true
fi
echo "✅ DB initialization done"

# --- Health checks ---
echo "=== STEP 7: Health checks (API + Web) ==="
API_OK=false
for i in $(seq 1 30); do
    STATUS=$(curl -s -o /dev/null -w "%{http_code}" -f http://localhost:5000/health 2>/dev/null || echo "000")
    if [ "$STATUS" = "200" ] || [ "$STATUS" = "301" ] || [ "$STATUS" = "302" ]; then
        API_OK=true
        echo "✅ API responded (attempt $i)"
        break
    fi
    sleep 2
done

WEB_OK=false
for i in $(seq 1 30); do
    if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
        WEB_OK=true
        echo "✅ Web responded (attempt $i)"
        break
    fi
    sleep 2
done

# --- Final status ---
echo "=== FINAL STATUS ==="
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

if [ "$API_OK" = "true" ] && [ "$WEB_OK" = "true" ]; then
    echo "🎉 Deployment healthy: API + Web up"
else
    echo "⚠️ Warning: one or more services didn't report healthy"
    echo "Check logs:"
    echo "  docker logs tms-sqlserver --tail=80"
    echo "  docker logs tms-api --tail=80"
    echo "  docker logs tms-web --tail=80"
fi
'''
                }
            }
        }
    }

    post {
        always {
            cleanWs()
        }
        failure {
            echo '❌ Pipeline failed. Check the logs above.'
        }
        success {
            echo '✅ Pipeline succeeded! Docker deployment complete.'
        }
    }
}
