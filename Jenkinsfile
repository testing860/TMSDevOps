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
                    #!/bin/bash
                    set -e
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
                    #!/bin/bash
                    set -e
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
                    #!/bin/bash
                    set -e
                    echo "🔧 Creating .env file..."
                    cat > .env << 'ENV_EOF'
DB_SERVER=sql-server
DB_NAME=TaskManagementSystem
DB_USER=sa
DB_PASSWORD=PLACEHOLDER_DB_PASSWORD

JWT_KEY=PLACEHOLDER_JWT_KEY
JWT_ISSUER=TMSAPI
JWT_AUDIENCE=TMSWebClient

ADMIN_EMAIL=admin@tms.com
ADMIN_PASSWORD=PLACEHOLDER_ADMIN_PASSWORD
ADMIN_DISPLAYNAME=Admin

ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
ENV_EOF

                    # Replace placeholders with actual credentials (shell will expand the env vars)
                    sed -i "s|PLACEHOLDER_DB_PASSWORD|$DB_PASSWORD|g" .env
                    sed -i "s|PLACEHOLDER_JWT_KEY|$JWT_KEY|g" .env
                    sed -i "s|PLACEHOLDER_ADMIN_PASSWORD|$ADMIN_PASSWORD|g" .env

                    # Lock down the .env until it's copied into DEPLOY_PATH
                    chmod 600 .env || true

                    echo "✅ .env file created"
                '''
            }
        }

        stage('Deploy Locally') {
            steps {
                // Use single-quoted script blocks so Groovy does not interpolate secrets
                sh '''
                    #!/bin/bash
                    set -euo pipefail

                    echo "🚀 Starting local Docker deployment"
                    echo "Workspace: $WORKSPACE"
                    echo "Deploy path: $DEPLOY_PATH"

                    cd "$WORKSPACE"

                    echo "=== Checking Docker installation ==="
                    if ! command -v docker >/dev/null 2>&1; then
                        echo "❌ Docker is not installed or not in PATH. Ensure the Jenkins agent has Docker."
                        exit 1
                    fi

                    # determine docker-compose command (support v1 and v2)
                    DC_CMD=""
                    if command -v docker-compose >/dev/null 2>&1; then
                        DC_CMD="docker-compose"
                    elif docker compose version >/dev/null 2>&1; then
                        DC_CMD="docker compose"
                    else
                        echo "❌ Docker Compose is not available (checked 'docker-compose' and 'docker compose')."
                        exit 1
                    fi

                    echo "✅ Docker: $(docker --version)"
                    echo "✅ Docker Compose: $($DC_CMD --version 2>/dev/null || true)"

                    echo "=== STEP 1: Stop old TMS systemd service (if present) ==="
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true

                    echo "=== STEP 2: Remove old nginx config (if present) ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true || true

                    echo "=== STEP 3: Prepare deployment directory ==="
                    sudo mkdir -p "$DEPLOY_PATH"
                    sudo cp -f .env "$DEPLOY_PATH/" || true
                    sudo chmod 600 "$DEPLOY_PATH/.env" || true

                    echo "Copying project files (patterns: *.yml, Dockerfile*, *.sln)..."
                    # safe loop to avoid Groovy parsing issues with find escaping
                    for pattern in *.yml Dockerfile* *.sln; do
                        # expand glob, copy files if exist
                        for f in $pattern; do
                            if [ -e "$f" ]; then
                                echo "Copying $f -> $DEPLOY_PATH/"
                                sudo cp -r "$f" "$DEPLOY_PATH/" || true
                            fi
                        done
                    done

                    # copy directories (projects)
                    if [ -d "TMS.API" ]; then
                        sudo rm -rf "$DEPLOY_PATH/TMS.API" 2>/dev/null || true
                        sudo cp -r TMS.API "$DEPLOY_PATH/" || true
                    fi
                    if [ -d "TMS.Web" ]; then
                        sudo rm -rf "$DEPLOY_PATH/TMS.Web" 2>/dev/null || true
                        sudo cp -r TMS.Web "$DEPLOY_PATH/" || true
                    fi
                    if [ -d "TMS.Shared" ]; then
                        sudo rm -rf "$DEPLOY_PATH/TMS.Shared" 2>/dev/null || true
                        sudo cp -r TMS.Shared "$DEPLOY_PATH/" || true
                    fi

                    sudo chown -R jenkins:jenkins "$DEPLOY_PATH/" || true
                    echo "✅ Files copied to $DEPLOY_PATH"

                    echo "=== STEP 4: Build and start Docker containers ==="
                    cd "$DEPLOY_PATH"

                    # stop existing containers for this compose
                    echo "Stopping any existing containers..."
                    sudo $DC_CMD down --remove-orphans || true

                    echo "Building Docker images..."
                    sudo $DC_CMD build --no-cache

                    echo "Starting containers..."
                    sudo $DC_CMD up -d

                    echo "=== STEP 5: Wait for SQL Server to be ready ==="
                    # Wait a bit for services to initialize
                    sleep 5

                    SQL_OK=false
                    for i in {1..30}; do
                        # try both common sqlcmd paths in container; ignore errors if container not yet ready
                        if sudo $DC_CMD exec -T sql-server /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DB_PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; then
                            SQL_OK=true
                            echo "✅ SQL Server is ready (attempt $i)"
                            break
                        fi
                        if sudo $DC_CMD exec -T sql-server /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$DB_PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; then
                            SQL_OK=true
                            echo "✅ SQL Server is ready (attempt $i)"
                            break
                        fi
                        echo "Waiting for SQL Server... (attempt $i/30)"
                        sleep 2
                    done

                    if [ "$SQL_OK" = "false" ]; then
                        echo "❌ SQL Server failed to start in time. Showing logs (tail 100):"
                        sudo $DC_CMD logs sql-server --tail=100 || true
                        exit 1
                    fi

                    echo "=== STEP 6: Wait for API to respond ==="
                    API_OK=false
                    for i in {1..20}; do
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
                    for i in {1..15}; do
                        if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                            WEB_OK=true
                            echo "✅ Web responded (attempt $i)"
                            break
                        fi
                        echo "Waiting for Web... (attempt $i/15)"
                        sleep 2
                    done

                    echo "=== STEP 8: Final verification & status ==="
                    sudo $DC_CMD ps || true

                    SERVER_IP="$(hostname -I | awk '{print $1}')"
                    echo "Server IP: $SERVER_IP"

                    API_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger || echo "000")
                    WEB_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:7130 || echo "000")

                    echo "API Status: $API_STATUS"
                    echo "Web Status: $WEB_STATUS"

                    if [ "$API_STATUS" = "200" ] && ( [ "$WEB_STATUS" = "200" ] || [ "$WEB_STATUS" = "304" ] ); then
                        echo "✅ All services are responding correctly!"
                    else
                        echo "⚠️ Some services may not be fully healthy. Showing last 200 log lines:"
                        sudo $DC_CMD logs --tail=200 || true
                    fi

                    echo ""
                    echo "========================================"
                    echo "🎉 DOCKER DEPLOYMENT COMPLETE"
                    echo "========================================"
                    echo "Web Interface:  http://$SERVER_IP:7130"
                    echo "API Swagger:    http://$SERVER_IP:5000/swagger"
                    echo "SQL Server:     $SERVER_IP:1433"
                    echo ""
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

