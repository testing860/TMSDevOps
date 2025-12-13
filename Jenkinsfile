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
                    echo "🚀 Starting local Docker deployment..."
                    
                    # Navigate to workspace
                    cd ${WORKSPACE}
                    
                    # Check if Docker Compose is available
                    echo "=== Checking Docker installation ==="
                    if ! command -v docker &> /dev/null; then
                        echo "❌ Docker is not installed!"
                        exit 1
                    fi
                    
                    if ! command -v docker-compose &> /dev/null; then
                        echo "❌ Docker Compose is not installed at /usr/local/bin/docker-compose!"
                        echo "✅ But we found it at: \$(which docker-compose || echo 'not found')"
                        exit 1
                    fi
                    
                    echo "✅ Docker: \$(docker --version)"
                    echo "✅ Docker Compose: \$(docker-compose --version)"
                    
                    # Stop old services
                    echo "=== STEP 1: Stop old TMS application services ==="
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true
                    
                    echo "=== STEP 2: Remove old nginx config ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true
                    
                    echo "=== STEP 3: Prepare deployment directory ==="
                    sudo mkdir -p ${DEPLOY_PATH}
                    sudo cp .env ${DEPLOY_PATH}/
                    sudo chmod 600 ${DEPLOY_PATH}/.env
                    
                    # Copy project files
                    echo "Copying project files..."
                    find . -maxdepth 1 -type f -name "*.yml" -o -name "Dockerfile*" -o -name "*.sln" | xargs -I {} sudo cp {} ${DEPLOY_PATH}/ 2>/dev/null || true
                    sudo cp -r TMS.API TMS.Web TMS.Shared ${DEPLOY_PATH}/
                    
                    sudo chown -R jenkins:jenkins ${DEPLOY_PATH}/
                    echo "✅ Files copied to deployment directory"
                    
                    echo "=== STEP 4: Build and start Docker containers ==="
                    cd ${DEPLOY_PATH}
                    
                    # Stop any existing containers
                    echo "Stopping any existing containers..."
                    sudo docker-compose down --remove-orphans 2>/dev/null || true
                    
                    # Build images
                    echo "Building Docker images..."
                    sudo docker-compose build --no-cache
                    
                    # Start containers
                    echo "Starting containers..."
                    sudo docker-compose up -d
                    
                    echo "=== STEP 5: Wait for services to be ready ==="
                    # Wait for SQL Server
                    echo "Waiting for SQL Server to start..."
                    sleep 10
                    
                    for i in {1..30}; do
                        if sudo docker-compose exec -T sql-server /opt/mssql-tools/bin/sqlcmd \\
                            -S localhost -U sa -P "\${DB_PASSWORD}" \\
                            -Q "SELECT 1" > /dev/null 2>&1; then
                            echo "✅ SQL Server is ready (attempt \$i)"
                            break
                        fi
                        echo "Waiting for SQL Server... (attempt \$i/30)"
                        sleep 2
                        if [ \$i -eq 30 ]; then
                            echo "❌ SQL Server failed to start in time"
                            sudo docker-compose logs sql-server
                            exit 1
                        fi
                    done
                    
                    # Wait for API
                    echo "Waiting for API to start..."
                    for i in {1..20}; do
                        if curl -s -f http://localhost:5000/swagger > /dev/null 2>&1; then
                            echo "✅ API is ready (attempt \$i)"
                            break
                        fi
                        echo "Waiting for API... (attempt \$i/20)"
                        sleep 3
                    done
                    
                    # Wait for Web
                    echo "Waiting for Web to start..."
                    for i in {1..15}; do
                        if curl -s -f http://localhost:7130 > /dev/null 2>&1; then
                            echo "✅ Web is ready (attempt \$i)"
                            break
                        fi
                        echo "Waiting for Web... (attempt \$i/15)"
                        sleep 2
                    done
                    
                    echo "=== STEP 6: Verify all containers are running ==="
                    sudo docker-compose ps
                    
                    echo "=== STEP 7: Get server IP ==="
                    SERVER_IP=\$(hostname -I | awk '{print \$1}')
                    echo "Server IP: \${SERVER_IP}"
                    
                    echo "=== STEP 8: Final verification ==="
                    API_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger || echo "000")
                    WEB_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:7130 || echo "000")
                    
                    echo "API Status: \$API_STATUS"
                    echo "Web Status: \$WEB_STATUS"
                    
                    if [ "\$API_STATUS" = "200" ] && ([ "\$WEB_STATUS" = "200" ] || [ "\$WEB_STATUS" = "304" ]); then
                        echo "✅ All services are responding correctly!"
                    else
                        echo "⚠️  Some services may not be fully healthy"
                        echo "Checking logs..."
                        sudo docker-compose logs --tail=20
                    fi
                    
                    echo ""
                    echo "========================================"
                    echo "🎉 DOCKER DEPLOYMENT COMPLETE"
                    echo "========================================"
                    echo "Your TMS application is now running in Docker containers:"
                    echo ""
                    echo "🐳 Containers:"
                    echo "   - tms-sqlserver (SQL Server)"
                    echo "   - tms-api (API backend)"
                    echo "   - tms-web (Blazor frontend)"
                    echo ""
                    echo "🌐 Access URLs:"
                    echo "   Web Interface:  http://\${SERVER_IP}:7130"
                    echo "   API Swagger:    http://\${SERVER_IP}:5000/swagger"
                    echo "   SQL Server:     \${SERVER_IP}:1433"
                    echo ""
                    echo "📋 Management commands:"
                    echo "   Check status:   cd ${DEPLOY_PATH} && sudo docker-compose ps"
                    echo "   View logs:      cd ${DEPLOY_PATH} && sudo docker-compose logs -f"
                    echo "   Stop services:  cd ${DEPLOY_PATH} && sudo docker-compose down"
                    echo "   Restart:        cd ${DEPLOY_PATH} && sudo docker-compose restart"
                    echo "========================================"
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
