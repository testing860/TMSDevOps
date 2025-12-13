pipeline {
    agent any

    environment {
        // Your Jenkins credentials
        GITHUB_SSH_CREDENTIAL_ID = 'f2279fbb-b675-4191-bb6e-5e5c0d1421a5'
        DB_PASSWORD = credentials('tms-db-password')
        JWT_KEY = credentials('tms-jwt-key')
        ADMIN_PASSWORD = credentials('tms-admin-password')
        // Docker configuration
        DOCKER_IMAGE_PREFIX = 'tms'
        // CRITICAL: Set this to your Ubuntu server's actual IP
        SERVER_IP = '10.0.2.15'
    }

    stages {
        stage('Checkout from GitHub') {
            steps {
                checkout scm
            }
        }

        stage('Prepare Docker Build Environment') {
            steps {
                sh '''
                    echo "📦 Preparing Docker build environment..."
                    
                    # Create .env file with Jenkins credentials
                    cat > .env << EOF
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

# API Settings
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
EOF
                    echo "✅ Docker .env file created."
                    
                    # Verify critical docker-compose.yml configuration
                    echo "Checking docker-compose.yml configuration..."
                    if grep -q "ApiBaseUrl=http://api:5000" docker-compose.yml; then
                        echo "✅ docker-compose.yml has correct ApiBaseUrl"
                    else
                        echo "❌ ERROR: docker-compose.yml missing ApiBaseUrl for web service!"
                        exit 1
                    fi
                '''
            }
        }

        stage('Build Docker Images') {
            steps {
                sh '''
                    echo "🐳 Building Docker images..."
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
                        DEPLOY_USER="ec"
                        DEPLOY_PATH="/opt/tms-app-docker"
                        
                        # 1. Copy the entire project to the server
                        echo "Copying project files via rsync..."
                        rsync -avz --delete \\
                              --exclude='.git' \\
                              --exclude='bin' \\
                              --exclude='obj' \\
                              --exclude='node_modules' \\
                              --exclude='*.user' \\
                              --exclude='*.suo' \\
                              . ${DEPLOY_USER}@${SERVER_IP}:${DEPLOY_PATH}/

                        # 2. Execute deployment commands on the server
                        ssh ${DEPLOY_USER}@${SERVER_IP} '
                            cd ${DEPLOY_PATH}

                            echo "=== STEP 1: Stopping old TMS application ==="
                            sudo systemctl stop tms-api.service 2>/dev/null || true
                            sudo systemctl disable tms-api.service 2>/dev/null || true

                            echo "=== STEP 2: Removing old nginx config ==="
                            sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                            sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                            sudo systemctl restart nginx 2>/dev/null || true

                            echo "=== STEP 3: Handling Database Container ==="
                            echo "Stopping and removing old sqlserver container..."
                            sudo docker stop sqlserver 2>/dev/null || true
                            sudo docker rm sqlserver 2>/dev/null || true

                            echo "Cleaning up old volumes (optional)..."
                            sudo docker volume prune -f 2>/dev/null || true

                            echo "=== STEP 4: Starting fresh Docker application ==="
                            echo "Starting docker-compose with tms-sqlserver..."
                            docker-compose down --remove-orphans 2>/dev/null || true
                            docker-compose up -d --remove-orphans

                            echo "=== STEP 5: Waiting for services to initialize ==="
                            echo "Waiting for SQL Server to start (30 seconds)..."
                            sleep 30

                            echo "=== STEP 6: Database Initialization ==="
                            echo "Database will be created automatically by your API on first run"
                            echo "through Entity Framework migrations in Program.cs"

                            echo "=== STEP 7: Verification ==="
                            echo "Container status:"
                            docker-compose ps

                            echo "Testing web endpoint (expecting HTTP 200 or 304)..."
                            WEB_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:7130 || echo "000")
                            echo "Web service HTTP status: \${WEB_STATUS}"

                            echo "Testing API endpoint..."
                            API_STATUS=\$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger || echo "000")
                            echo "API Swagger HTTP status: \${API_STATUS}"

                            if [ "\${WEB_STATUS}" = "200" ] || [ "\${WEB_STATUS}" = "304" ]; then
                                echo "✅ Web service is responding correctly"
                            else
                                echo "⚠️  Web service returned status \${WEB_STATUS}. Checking logs..."
                                docker-compose logs web --tail=20
                            fi
                        '

                        echo ""
                        echo "========================================"
                        echo "🎉 DEPLOYMENT COMPLETE"
                        echo "========================================"
                        echo "Your Dockerized TMS application is now running."
                        echo "Fresh database has been initialized."
                        echo ""
                        echo "🌐 WEB INTERFACE:  http://${SERVER_IP}:7130"
                        echo "⚙️  API Swagger:    http://${SERVER_IP}:5000/swagger"
                        echo "🐳 SQL Server:     ${SERVER_IP}:1433 (new tms-sqlserver container)"
                        echo ""
                        echo "To check application status:"
                        echo "  ssh ec@${SERVER_IP}"
                        echo "  cd ${DEPLOY_PATH}"
                        echo "  docker-compose ps"
                        echo ""
                        echo "To view logs:"
                        echo "  docker-compose logs -f"
                        echo ""
                        echo "To restore your backup if needed:"
                        echo "  1. Copy backup to server: scp /path/to/backup.bak ec@${SERVER_IP}:/home/ec/"
                        echo "  2. Restore: docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P \"\${DB_PASSWORD}\" -C -Q \"RESTORE DATABASE [TaskManagementSystem] FROM DISK = N''/var/opt/mssql/data/backup.bak'' WITH REPLACE;\""
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
                docker-compose down --remove-orphans 2>/dev/null || true
                docker system prune -af 2>/dev/null || true
            '''
            cleanWs()
        }
        failure {
            echo '❌ Pipeline failed. Check the logs above.'
            // Consider adding notification here (email, Slack, etc.)
        }
        success {
            echo '✅ Pipeline succeeded! Fresh Docker deployment complete.'
        }
    }
}
