pipeline {
    agent any

    environment {
        GITHUB_SSH_CREDENTIAL_ID = 'f2279fbb-b675-4191-bb6e-5e5c0d1421a5'
        DB_PASSWORD = credentials('tms-db-password')
        JWT_KEY = credentials('tms-jwt-key')
        ADMIN_PASSWORD = credentials('tms-admin-password')
        // Docker configuration
        DOCKER_IMAGE_PREFIX = 'tms'
        // CRITICAL: Set this to your Ubuntu server's actual IP
        SERVER_IP = '10.0.2.15'
        DEPLOY_USER = 'ec'
        DEPLOY_PATH = '/opt/tms-app-docker'
    }

    stages {
        stage('Checkout from GitHub') {
            steps {
                checkout scm
            }
        }

        stage('Build & Test Application') {
            steps {
                sh '''
                    echo "Building .NET solution..."
                    dotnet build --configuration Release
                    echo "Running tests..."
                    dotnet test --configuration Release --logger "trx"
                '''
            }
        }

        stage('Prepare Docker Environment') {
            steps {
                // Securely create .env file without exposing in logs
                script {
                    envFileContent = """
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
"""
                    writeFile file: '.env', text: envFileContent
                }
                
                sh '''
                    echo "✅ Secure .env file created"
                    # Verify docker-compose configuration
                    echo "Checking docker-compose.yml..."
                    if grep -q "container_name: tms-sqlserver" docker-compose.yml; then
                        echo "✅ docker-compose.yml has correct container names"
                    fi
                '''
            }
        }

        stage('Build Docker Images Locally') {
            steps {
                sh '''
                    echo "🐳 Building Docker images locally for verification..."
                    docker-compose build --no-cache
                    echo "✅ Images built successfully."
                    docker images | grep tms-
                '''
            }
        }

        stage('Deploy to Ubuntu Server') {
            steps {
                sshagent(['ubuntu-server-ssh-credentials']) {
                    sh """
                        echo "🚀 Starting deployment to server..."
                        
                        # 1. Copy project files EXCLUDING sensitive data and existing docker volumes
                        echo "Copying project files via rsync..."
                        rsync -avz --delete \\
                              --exclude='.git' \\
                              --exclude='sqlserver-data' \\
                              --exclude='tms-sql-data' \\
                              --exclude='node_modules' \\
                              --exclude='bin' \\
                              --exclude='obj' \\
                              --exclude='*.user' \\
                              --exclude='*.suo' \\
                              . ${DEPLOY_USER}@${SERVER_IP}:${DEPLOY_PATH}/

                        # 2. Execute deployment commands on the server
                        ssh ${DEPLOY_USER}@${SERVER_IP} '
                            set -e  # Exit on any error
                            cd ${DEPLOY_PATH}
                            
                            echo "=== STEP 1: Stop old TMS application services ==="
                            # Only stop the containers managed by this docker-compose
                            docker-compose down --remove-orphans 2>/dev/null || true
                            
                            echo "=== STEP 2: Create secure .env file on server ==="
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
                            chmod 600 .env
                            echo "✅ .env file created with secure permissions"
                            
                            echo "=== STEP 3: Build Docker images ==="
                            docker-compose build --no-cache
                            
                            echo "=== STEP 4: Start SQL Server first ==="
                            docker-compose up -d sql-server
                            
                            echo "=== STEP 5: Wait for SQL Server to be ready ==="
                            echo "Waiting for SQL Server to start (max 60 seconds)..."
                            for i in {1..30}; do
                                if docker-compose exec -T sql-server /opt/mssql-tools/bin/sqlcmd \\
                                    -S localhost -U sa -P "${DB_PASSWORD}" \\
                                    -Q "SELECT 1" > /dev/null 2>&1; then
                                    echo "✅ SQL Server is ready (attempt \$i)"
                                    break
                                fi
                                echo "Waiting for SQL Server... (attempt \$i/30)"
                                sleep 2
                                if [ \$i -eq 30 ]; then
                                    echo "❌ SQL Server failed to start in time"
                                    docker-compose logs sql-server
                                    exit 1
                                fi
                            done
                            
                            echo "=== STEP 6: Start API and Web services ==="
                            docker-compose up -d api web
                            
                            echo "=== STEP 7: Wait for services to be ready ==="
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
                            
                            echo "=== STEP 8: Verify all containers are running ==="
                            docker-compose ps
                            
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
                                docker-compose logs --tail=20
                            fi
                        '
                        
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
                        echo "   Web Interface:  http://${SERVER_IP}:7130"
                        echo "   API Swagger:    http://${SERVER_IP}:5000/swagger"
                        echo "   SQL Server:     ${SERVER_IP}:1433"
                        echo ""
                        echo "📋 Management commands (run on server):"
                        echo "   Check status:   cd ${DEPLOY_PATH} && docker-compose ps"
                        echo "   View logs:      cd ${DEPLOY_PATH} && docker-compose logs -f"
                        echo "   Stop services:  cd ${DEPLOY_PATH} && docker-compose down"
                        echo "   Restart:        cd ${DEPLOY_PATH} && docker-compose restart"
                        echo "========================================"
                    """
                }
            }
        }
    }

    post {
        always {
            // Clean up the Jenkins agent
            sh '''
                echo "🧹 Cleaning up Jenkins workspace..."
                # Remove sensitive files first
                rm -f .env 2>/dev/null || true
                # Stop and remove any containers from local build
                docker-compose down --remove-orphans 2>/dev/null || true
                # Clean up Docker resources
                docker system prune -af 2>/dev/null || true
            '''
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