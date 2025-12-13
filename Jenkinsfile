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
                    
                    # Store credentials in temporary variables
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
                sh '''
                    set -e  # Exit on error, but not pipefail
                    echo "🚀 Starting local Docker deployment"
                    echo "Workspace: ${WORKSPACE}"
                    echo "Deploy path: ${DEPLOY_PATH}"

                    cd "${WORKSPACE}"

                    echo "=== Checking Docker installation ==="
                    if ! command -v docker >/dev/null 2>&1; then
                        echo "❌ Docker is not installed"
                        exit 1
                    fi

                    # Check for docker-compose (v1) or docker compose (v2)
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

                    echo "=== STEP 1: Stop old TMS services ==="
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true

                    echo "=== STEP 2: Remove old nginx config ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true

                    echo "=== STEP 3: Prepare deployment directory ==="
                    sudo mkdir -p "${DEPLOY_PATH}"
                    sudo cp -f .env "${DEPLOY_PATH}/" || true
                    sudo chmod 600 "${DEPLOY_PATH}/.env" || true

                    echo "Copying project files..."
                    sudo cp -f docker-compose.yml Dockerfile "${DEPLOY_PATH}/" 2>/dev/null || true
                    sudo cp -f TMS.Web/Dockerfile "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    sudo cp -f TMS.Web/nginx.conf "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    
                    # Copy project directories
                    sudo rm -rf "${DEPLOY_PATH}/TMS.API" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Web" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Shared" 2>/dev/null || true
                    
                    sudo cp -r TMS.API TMS.Web TMS.Shared "${DEPLOY_PATH}/" || true
                    sudo chown -R jenkins:jenkins "${DEPLOY_PATH}/" || true
                    echo "✅ Files copied to ${DEPLOY_PATH}"

                    echo "=== STEP 4: Build and start Docker containers ==="
                    cd "${DEPLOY_PATH}"

                    echo "Stopping any existing containers..."
                    ${DC_CMD} down --remove-orphans 2>/dev/null || true

                    echo "Building Docker images..."
                    ${DC_CMD} build --no-cache

                    echo "Starting containers..."
                    ${DC_CMD} up -d

                    echo "=== STEP 5: Wait for SQL Server to be ready ==="
                    sleep 10
                    
                    SQL_OK=false
                    for i in $(seq 1 30); do
                        # Try to connect to SQL Server
                        if ${DC_CMD} exec -T sql-server /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" >/dev/null 2>&1; then
                            SQL_OK=true
                            echo "✅ SQL Server is ready (attempt $i)"
                            break
                        fi
                        echo "Waiting for SQL Server... (attempt $i/30)"
                        sleep 2
                    done

                    if [ "$SQL_OK" = "false" ]; then
                        echo "❌ SQL Server failed to start"
                        ${DC_CMD} logs sql-server --tail=50 || true
                        exit 1
                    fi

                    echo "=== STEP 6: Wait for API to respond ==="
                    API_OK=false
                    for i in $(seq 1 20); do
                        if curl -s -f http://localhost:5000/swagger >/dev/null 2>&1; then
                            API_OK=true
                            echo "✅ API responded (attempt $i)"
                            break
                        fi
                        echo "Waiting for API... (attempt $i/20)"
                        sleep 3
                    done

                    echo "=== STEP 7: Wait for Web to respond ==="
                    WEB_OK=false
                    for i in $(seq 1 15); do
                        if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                            WEB_OK=true
                            echo "✅ Web responded (attempt $i)"
                            break
                        fi
                        echo "Waiting for Web... (attempt $i/15)"
                        sleep 2
                    done

                    echo "=== STEP 8: Final verification ==="
                    ${DC_CMD} ps || true

                    SERVER_IP=$(hostname -I | awk '{print $1}')
                    echo "Server IP: $SERVER_IP"

                    API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger || echo "000")
                    WEB_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:7130 || echo "000")

                    echo "API Status: $API_STATUS"
                    echo "Web Status: $WEB_STATUS"

                    if [ "$API_STATUS" = "200" ] && ( [ "$WEB_STATUS" = "200" ] || [ "$WEB_STATUS" = "304" ] ); then
                        echo "✅ All services are responding correctly!"
                        echo ""
                        echo "🎉 DOCKER DEPLOYMENT COMPLETE"
                        echo "Web Interface:  http://$SERVER_IP:7130"
                        echo "API Swagger:    http://$SERVER_IP:5000/swagger"
                        echo "SQL Server:     $SERVER_IP:1433"
                    else
                        echo "⚠️ Some services may not be fully healthy"
                        ${DC_CMD} logs --tail=50 || true
                        exit 1
                    fi
                '''
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
