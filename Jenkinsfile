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
                    dotnet test --configuration Release --logger "trx" --no-build
                '''
            }
        }

        stage('Prepare Environment') {
            steps {
                sh '''
                    echo "🔧 Creating .env file..."
                    # Create .env file with masked credentials
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
                    
                    # Replace placeholders with actual credentials
                    sed -i "s|PLACEHOLDER_DB_PASSWORD|${DB_PASSWORD}|g" .env
                    sed -i "s|PLACEHOLDER_JWT_KEY|${JWT_KEY}|g" .env
                    sed -i "s|PLACEHOLDER_ADMIN_PASSWORD|${ADMIN_PASSWORD}|g" .env
                    
                    echo "✅ .env file created"
                '''
            }
        }

        stage('Deploy Locally') {
            steps {
                sh """
                    # run under bash
                    set -euo pipefail
                    echo "🚀 Starting local Docker deployment (bash)"

                    cd "${WORKSPACE}"

                    echo "=== Checking Docker installation ==="
                    DOCKER_CMD=""
                    if command -v docker > /dev/null 2>&1; then
                        DOCKER_CMD="\$(command -v docker)"
                    fi

                    # Check docker-compose (v1) or docker compose (v2)
                    DOCKER_COMPOSE_CMD=""
                    if command -v docker-compose > /dev/null 2>&1; then
                        DOCKER_COMPOSE_CMD="\$(command -v docker-compose)"
                    elif \${DOCKER_CMD:+true} && \${DOCKER_CMD} compose version > /dev/null 2>&1; then
                        DOCKER_COMPOSE_CMD="\${DOCKER_CMD} compose"
                    fi

                    if [ -z "\${DOCKER_CMD}" ]; then
                        echo "❌ Docker is not installed or not in PATH. Ensure the Jenkins agent has Docker."
                        exit 1
                    fi

                    if [ -z "\${DOCKER_COMPOSE_CMD}" ]; then
                        echo "❌ Docker Compose is not available (checked 'docker-compose' and 'docker compose')."
                        exit 1
                    fi

                    echo "✅ Docker: \$(\${DOCKER_CMD} --version)"
                    echo "✅ Docker Compose: \$(\${DOCKER_COMPOSE_CMD} --version 2>/dev/null || true)"

                    echo "=== STEP 1: Stop old TMS application services ==="
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true

                    echo "=== STEP 2: Remove old nginx config ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true

                    echo "=== STEP 3: Prepare deployment directory ==="
                    sudo mkdir -p "${DEPLOY_PATH}"
                    sudo cp -f .env "${DEPLOY_PATH}/"
                    sudo chmod 600 "${DEPLOY_PATH}/.env" || true

                    echo "Copying project files..."
                    # safer find grouping and copy
                    find . -maxdepth 1 \\( -name "*.yml" -o -name "Dockerfile*" -o -name "*.sln" \\) -type f -print0 | xargs -0 -r sudo cp -t "${DEPLOY_PATH}" || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.API" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Web" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Shared" 2>/dev/null || true
                    sudo cp -r TMS.API TMS.Web TMS.Shared "${DEPLOY_PATH}/" || true

                    sudo chown -R jenkins:jenkins "${DEPLOY_PATH}/"
                    echo "✅ Files copied to deployment directory"

                    echo "=== STEP 4: Build and start Docker containers ==="
                    cd "${DEPLOY_PATH}"

                    echo "Stopping any existing containers..."
                    sudo \${DOCKER_COMPOSE_CMD} down --remove-orphans 2>/dev/null || true

                    echo "Building Docker images..."
                    sudo \${DOCKER_COMPOSE_CMD} build --no-cache

                    echo "Starting containers..."
                    sudo \${DOCKER_COMPOSE_CMD} up -d

                    echo "=== STEP 5: Wait for services to be ready ==="
                    # Wait for SQL Server - choose sqlcmd path inside container robustly
                    echo "Waiting for SQL Server to start..."
                    sleep 5

                    # determine sqlcmd path inside the container; use docker-compose exec to run a small check
                    SQLCMD_INSIDE=""
                    if sudo \${DOCKER_COMPOSE_CMD} exec -T sql-server test -x /opt/mssql-tools18/bin/sqlcmd 2>/dev/null; then
                        SQLCMD_INSIDE="/opt/mssql-tools18/bin/sqlcmd"
                    elif sudo \${DOCKER_COMPOSE_CMD} exec -T sql-server test -x /opt/mssql-tools/bin/sqlcmd 2>/dev/null; then
                        SQLCMD_INSIDE="/opt/mssql-tools/bin/sqlcmd"
                    else
                        # fallback: try to locate
                        if sudo \${DOCKER_COMPOSE_CMD} exec -T sql-server sh -c "command -v sqlcmd >/dev/null 2>&1" 2>/dev/null; then
                            SQLCMD_INSIDE="\$(sudo \${DOCKER_COMPOSE_CMD} exec -T sql-server sh -c 'command -v sqlcmd' 2>/dev/null)"
                        fi
                    fi

                    if [ -z "\${SQLCMD_INSIDE}" ]; then
                        echo "⚠️ Could not detect sqlcmd path inside sql-server container; skipping SQL server health check."
                    else
                        echo "Detected sqlcmd inside container: \${SQLCMD_INSIDE}"
                        for i in \$(seq 1 30); do
                            if sudo \${DOCKER_COMPOSE_CMD} exec -T sql-server \${SQLCMD_INSIDE} -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" > /dev/null 2>&1; then
                                echo "✅ SQL Server is ready (attempt \$i)"
                                break
                            fi
                            echo "Waiting for SQL Server... (attempt \$i/30)"
                            sleep 2
                            if [ "\$i" = "30" ]; then
                                echo "❌ SQL Server failed to start in time"
                                sudo \${DOCKER_COMPOSE_CMD} logs sql-server --tail=50
                                exit 1
                            fi
                        done
                    fi

                    # Wait for API
                    echo "Waiting for API to start..."
                    for i in \$(seq 1 20); do
                        if curl -s -f http://localhost:5000/swagger > /dev/null 2>&1; then
                            echo "✅ API is ready (attempt \$i)"
                            break
                        fi
                        echo "Waiting for API... (attempt \$i/20)"
                        sleep 3
                    done

                    # Wait for Web
                    echo "Waiting for Web to start..."
                    for i in \$(seq 1 15); do
                        if curl -s -f http://localhost:7130 > /dev/null 2>&1; then
                            echo "✅ Web is ready (attempt \$i)"
                            break
                        fi
                        echo "Waiting for Web... (attempt \$i/15)"
                        sleep 2
                    done

                    echo "=== STEP 6: Verify all containers are running ==="
                    sudo \${DOCKER_COMPOSE_CMD} ps

                    SERVER_IP=\$(hostname -I | awk '{print \$1}')
                    echo "Server IP: \${SERVER_IP}"

                    API_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger || echo "000")
                    WEB_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:7130 || echo "000")

                    echo "API Status: \$API_STATUS"
                    echo "Web Status: \$WEB_STATUS"

                    if [ "\$API_STATUS" = "200" ] && ( [ "\$WEB_STATUS" = "200" ] || [ "\$WEB_STATUS" = "304" ] ); then
                        echo "✅ All services are responding correctly!"
                    else
                        echo "⚠️  Some services may not be fully healthy"
                        echo "Checking last logs..."
                        sudo \${DOCKER_COMPOSE_CMD} logs --tail=50
                    fi

                    echo ""
                    echo "🎉 DOCKER DEPLOYMENT COMPLETE"
                    echo "Web Interface:  http://\${SERVER_IP}:7130"
                    echo "API Swagger:    http://\${SERVER_IP}:5000/swagger"
                    echo "SQL Server:     \${SERVER_IP}:1433"
                """
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
