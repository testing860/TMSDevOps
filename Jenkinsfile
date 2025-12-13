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

        stage('Deploy Locally') {
            steps {
                sh """
                    echo "🚀 Starting local Docker deployment..."
                    
                    # Navigate to workspace
                    cd ${WORKSPACE}
                    
                    # Stop old services
                    echo "=== STEP 1: Stop old TMS application services ==="
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true
                    
                    echo "=== STEP 2: Remove old nginx config ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true
                    
                    echo "=== STEP 3: Create secure .env file ==="
                    sudo mkdir -p ${DEPLOY_PATH}
                    cat > .env << "EOF"
DB_SERVER=sql-server
DB_NAME=TaskManagementSystem
DB_USER=sa
DB_PASSWORD=${DB_PASSWORD}

JWT_KEY=${JWT_KEY}
JWT_ISSUER=TMSAPI
JWT_AUDIENCE=TMSWebClient

ADMIN_EMAIL=admin@tms.com
ADMIN_PASSWORD=${ADMIN_PASSWORD}
ADMIN_DISPLAYNAME=Admin

ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
EOF
                    sudo cp .env ${DEPLOY_PATH}/
                    sudo chmod 600 ${DEPLOY_PATH}/.env
                    echo "✅ .env file created with secure permissions"
                    
                    echo "=== STEP 4: Copy project to deployment directory ==="
                    sudo cp -r . ${DEPLOY_PATH}/
                    sudo chown -R ${USER}:${USER} ${DEPLOY_PATH}/
                    
                    echo "=== STEP 5: Build and start Docker containers ==="
                    cd ${DEPLOY_PATH}
                    
                    # Check if Docker Compose is installed
                    if ! command -v docker-compose &> /dev/null; then
                        echo "❌ Docker Compose not found!"
                        echo "Installing Docker and Docker Compose..."
                        sudo apt-get update
                        sudo apt-get install -y docker.io docker-compose
                        sudo usermod -aG docker ${USER}
                        echo "Log out and back in for group changes to take effect"
                        echo "Please restart Jenkins after installation"
                        exit 1
                    fi
                    
                    # Stop any existing containers
                    sudo docker-compose down --remove-orphans 2>/dev/null || true
                    
                    # Build and start
                    echo "Building Docker images..."
                    sudo docker-compose build --no-cache
                    
                    echo "Starting containers..."
                    sudo docker-compose up -d
                    
                    echo "=== STEP 6: Wait for services to be ready ==="
                    # Wait for SQL Server
                    echo "Waiting for SQL Server to start..."
                    for i in {1..30}; do
                        if sudo docker-compose exec -T sql-server /opt/mssql-tools/bin/sqlcmd \\
                            -S localhost -U sa -P "${DB_PASSWORD}" \\
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
                    
                    echo "=== STEP 7: Verify all containers are running ==="
                    sudo docker-compose ps
                    
                    echo "=== STEP 8: Get server IP ==="
                    SERVER_IP=\$(hostname -I | awk '{print \$1}')
                    echo "Server IP: \${SERVER_IP}"
                    
                    echo "=== STEP 9: Final verification ==="
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
