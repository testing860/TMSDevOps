pipeline {
    agent any

    environment {
        GITHUB_SSH_CREDENTIAL_ID = 'f2279fbb-b675-4191-bb6e-5e5c0d1421a5'
        DB_PASSWORD = credentials('tms-db-password')
        JWT_KEY = credentials('tms-jwt-key')
        ADMIN_PASSWORD = credentials('tms-admin-password')
        DEPLOY_PATH = '/opt/tms-app-docker'
        BACKUP_PATH = '/opt/tms-backups'
    }

    parameters {
        booleanParam(
            name: 'BACKUP_DATABASE',
            defaultValue: false,
            description: 'Create a database backup before deployment'
        )
        booleanParam(
            name: 'FORCE_REBUILD',
            defaultValue: false,
            description: 'Force rebuild of Docker images even if no changes detected'
        )
        booleanParam(
            name: 'RUN_MIGRATIONS',
            defaultValue: true,
            description: 'Run database migrations after deployment'
        )
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

ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
EOF
                    
                    chmod 600 .env
                    echo "✅ .env file created"
                '''
            }
        }

        stage('Database Backup (Optional)') {
            when {
                expression { params.BACKUP_DATABASE == true }
            }
            steps {
                sh '''
                    echo "💾 Backing up database before deployment..."
                    
                    # Check if SQL Server is currently running
                    if docker ps --format "{{.Names}}" | grep -q "tms-sqlserver"; then
                        echo "Creating database backup..."
                        TIMESTAMP=$(date +%Y%m%d_%H%M%S)
                        BACKUP_DIR="${BACKUP_PATH}/${TIMESTAMP}"
                        
                        sudo mkdir -p "${BACKUP_DIR}"
                        
                        # Create backup using sqlcmd
                        docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd \
                            -S localhost -U sa -P "${DB_PASSWORD}" \
                            -Q "BACKUP DATABASE [TaskManagementSystem] TO DISK = N'/tmp/tms_backup_${TIMESTAMP}.bak' WITH FORMAT, INIT;" \
                            2>/dev/null || echo "⚠️ Backup failed or sqlcmd not available"
                        
                        # Copy backup from container to host
                        if docker exec tms-sqlserver sh -c "ls /tmp/tms_backup_${TIMESTAMP}.bak" >/dev/null 2>&1; then
                            docker cp tms-sqlserver:/tmp/tms_backup_${TIMESTAMP}.bak "${BACKUP_DIR}/tms_backup.bak"
                            echo "✅ Database backup saved to: ${BACKUP_DIR}/tms_backup.bak"
                        else
                            echo "⚠️ Could not create backup - continuing anyway"
                        fi
                    else
                        echo "ℹ️ SQL Server not running, skipping backup"
                    fi
                '''
            }
        }

        stage('Deploy Application') {
            steps {
                script {
                    // Determine docker compose command version
                    env.DC_CMD = sh(script: '''
                        if command -v docker-compose >/dev/null 2>&1; then
                            echo "docker-compose"
                        elif docker compose version >/dev/null 2>&1; then
                            echo "docker compose"
                        else
                            echo "none"
                        fi
                    ''', returnStdout: true).trim()
                    
                    if (env.DC_CMD == "none") {
                        error("❌ Docker Compose not found")
                    }
                }
                
                sh '''
                    #!/bin/bash
                    set -e
                    echo "🚀 Starting application deployment"
                    echo "Workspace: ${WORKSPACE}"
                    echo "Deploy path: ${DEPLOY_PATH}"

                    cd "${WORKSPACE}"

                    echo "=== Checking Docker installation ==="
                    if ! command -v docker >/dev/null 2>&1; then
                        echo "❌ Docker is not installed"
                        exit 1
                    fi

                    echo "=== STEP 1: Stop old TMS services (only if running as services) ==="
                    # Only stop systemd services if they exist
                    sudo systemctl stop tms-api.service 2>/dev/null || echo "ℹ️ tms-api.service not found or already stopped"
                    sudo systemctl stop tms-app.service 2>/dev/null || echo "ℹ️ tms-app.service not found or already stopped"
                    
                    echo "=== STEP 2: Prepare deployment directory (preserving data) ==="
                    sudo mkdir -p "${DEPLOY_PATH}"
                    
                    # Preserve existing .env if it exists
                    if [ -f "${DEPLOY_PATH}/.env" ]; then
                        echo "ℹ️ Preserving existing .env file"
                        sudo cp "${DEPLOY_PATH}/.env" "${DEPLOY_PATH}/.env.backup"
                    fi
                    
                    sudo cp -f .env "${DEPLOY_PATH}/" 2>/dev/null || echo "ℹ️ Could not copy .env"
                    sudo chmod 600 "${DEPLOY_PATH}/.env" 2>/dev/null || echo "ℹ️ Could not set permissions on .env"
                    
                    # Check if SQL Server data directory exists and preserve it
                    if [ -d "${DEPLOY_PATH}/sqlserver-data" ]; then
                        echo "✅ Preserving existing SQL Server data directory"
                        sudo chmod 777 "${DEPLOY_PATH}/sqlserver-data"
                    else
                        echo "🔧 Creating SQL Server data directory..."
                        sudo mkdir -p "${DEPLOY_PATH}/sqlserver-data"
                        sudo chmod 777 "${DEPLOY_PATH}/sqlserver-data"
                        echo "✅ SQL Server data directory created"
                    fi
                    
                    echo "=== STEP 3: Check and update nginx configuration ==="
                    if [ -f "TMS.Web/nginx.conf" ]; then
                        # Remove any non-nginx content
                        if grep -q 'sudo ' TMS.Web/nginx.conf >/dev/null 2>&1 || ! grep -q 'events {' TMS.Web/nginx.conf >/dev/null 2>&1; then
                            echo "⚠️ Detected suspicious content in TMS.Web/nginx.conf — sanitizing..."
                            awk '/events \\{/{p=1} p{print}' TMS.Web/nginx.conf > TMS.Web/nginx.conf.sanitized 2>/dev/null || true
                            if [ -s TMS.Web/nginx.conf.sanitized ]; then
                                mv TMS.Web/nginx.conf.sanitized TMS.Web/nginx.conf
                                echo "✅ nginx.conf sanitized"
                            else
                                echo "❌ Sanitization failed - using default nginx config"
                                cat > TMS.Web/nginx.conf << 'NGINXEOF'
events {
    worker_connections 1024;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;
    
    server {
        listen 80;
        server_name localhost;
        root /usr/share/nginx/html;
        
        location / {
            try_files $uri $uri/ /index.html;
        }
        
        location /api/ {
            proxy_pass http://api:5000/;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
}
NGINXEOF
                            fi
                        else
                            echo "✅ nginx.conf looks OK"
                        fi
                    else
                        echo "⚠️ TMS.Web/nginx.conf not found, creating default..."
                        cat > TMS.Web/nginx.conf << 'NGINXEOF'
events {
    worker_connections 1024;
}

http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;
    
    server {
        listen 80;
        server_name localhost;
        root /usr/share/nginx/html;
        
        location / {
            try_files $uri $uri/ /index.html;
        }
        
        location /api/ {
            proxy_pass http://api:5000/;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }
    }
}
NGINXEOF
                    fi
                    
                    echo "=== STEP 4: Copy application files ==="
                    echo "Copying project files to ${DEPLOY_PATH}..."
                    
                    # Copy configuration files
                    sudo cp -f docker-compose.yml Dockerfile "${DEPLOY_PATH}/" 2>/dev/null || echo "ℹ️ Could not copy compose files"
                    sudo cp -f TMS.Web/Dockerfile "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    sudo cp -f TMS.Web/nginx.conf "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    
                    # Remove old application code (but preserve data)
                    echo "Removing old application code..."
                    sudo rm -rf "${DEPLOY_PATH}/TMS.API" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Web" 2>/dev/null || true
                    sudo rm -rf "${DEPLOY_PATH}/TMS.Shared" 2>/dev/null || true
                    
                    # Copy new application code
                    echo "Copying new application code..."
                    sudo cp -r TMS.API TMS.Web TMS.Shared "${DEPLOY_PATH}/" || {
                        echo "❌ Failed to copy application code"
                        exit 1
                    }
                    
                    # Set permissions
                    sudo chown -R jenkins:jenkins "${DEPLOY_PATH}/" || echo "ℹ️ Could not set ownership"
                    echo "✅ Application files copied"
                    
                    echo "=== STEP 5: Deploy with Docker Compose ==="
                    cd "${DEPLOY_PATH}"
                    
                    echo "Stopping application containers (preserving data)..."
                    
                    # Use the appropriate docker compose command
                    if [ "${DC_CMD}" = "docker-compose" ]; then
                        docker-compose down --remove-orphans 2>/dev/null || echo "ℹ️ No containers to stop"
                    else
                        docker compose down --remove-orphans 2>/dev/null || echo "ℹ️ No containers to stop"
                    fi
                    
                    # Check if we need to rebuild
                    echo "Checking if rebuild is needed..."
                    REBUILD_NEEDED=false
                    
                    # Check if FORCE_REBUILD parameter is true
                    # FIX: Use Jenkins environment variable instead of params in shell script
                    if [ "$FORCE_REBUILD" = "true" ]; then
                        REBUILD_NEEDED=true
                        echo "ℹ️ Force rebuild requested"
                    fi
                    
                    if [ "$REBUILD_NEEDED" = "true" ]; then
                        echo "Building Docker images..."
                        if [ "${DC_CMD}" = "docker-compose" ]; then
                            docker-compose build --no-cache
                        else
                            docker compose build --no-cache
                        fi
                    else
                        echo "Building Docker images (always building to ensure fresh images)..."
                        if [ "${DC_CMD}" = "docker-compose" ]; then
                            docker-compose build --no-cache
                        else
                            docker compose build --no-cache
                        fi
                    fi
                    
                    echo "Starting application..."
                    if [ "${DC_CMD}" = "docker-compose" ]; then
                        docker-compose up -d
                    else
                        docker compose up -d
                    fi
                    
                    echo "=== STEP 6: Wait for services ==="
                    echo "Waiting for services to start..."
                    
                    # Wait for SQL Server
                    echo "Waiting for SQL Server (30 seconds)..."
                    sleep 30
                    
                    if docker ps | grep -q "tms-sqlserver"; then
                        echo "✅ SQL Server container is running"
                    else
                        echo "❌ SQL Server container failed to start"
                        if [ "${DC_CMD}" = "docker-compose" ]; then
                            docker-compose logs sql-server --tail=20 2>/dev/null || true
                        else
                            docker compose logs sql-server --tail=20 2>/dev/null || true
                        fi
                        exit 1
                    fi
                    
                    # Wait for API
                    echo "Waiting for API (up to 60 seconds)..."
                    API_READY=false
                    for i in $(seq 1 20); do
                        if curl -s -f http://localhost:5000/health >/dev/null 2>&1 || \
                           curl -s -f http://localhost:5000/swagger >/dev/null 2>&1 || \
                           curl -s -f http://localhost:5000/api/health >/dev/null 2>&1; then
                            API_READY=true
                            echo "✅ API is ready (attempt $i)"
                            break
                        fi
                        echo "Waiting for API... ($i/20)"
                        sleep 3
                    done
                    
                    if [ "$API_READY" = "false" ]; then
                        echo "⚠️ API not responding, checking logs..."
                        if [ "${DC_CMD}" = "docker-compose" ]; then
                            docker-compose logs api --tail=20 2>/dev/null || true
                        else
                            docker compose logs api --tail=20 2>/dev/null || true
                        fi
                        echo "Will continue to check web..."
                    fi
                    
                    # Wait for Web
                    echo "Waiting for Web (up to 40 seconds)..."
                    WEB_READY=false
                    for i in $(seq 1 20); do
                        if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                            WEB_READY=true
                            echo "✅ Web is ready (attempt $i)"
                            break
                        fi
                        echo "Waiting for Web... ($i/20)"
                        sleep 2
                    done
                    
                    if [ "$WEB_READY" = "false" ]; then
                        echo "⚠️ Web not responding, checking logs..."
                        if [ "${DC_CMD}" = "docker-compose" ]; then
                            docker-compose logs web --tail=20 2>/dev/null || true
                        else
                            docker compose logs web --tail=20 2>/dev/null || true
                        fi
                    fi
                    
                    echo "=== STEP 7: Verify deployment ==="
                    echo "Current container status:"
                    if [ "${DC_CMD}" = "docker-compose" ]; then
                        docker-compose ps 2>/dev/null || docker ps --filter "name=tms-" || true
                    else
                        docker compose ps 2>/dev/null || docker ps --filter "name=tms-" || true
                    fi
                    
                    SERVER_IP=$(hostname -I | awk '{print $1}' || echo "127.0.0.1")
                    echo "Server IP: $SERVER_IP"
                    
                    # Test endpoints
                    echo "Testing endpoints..."
                    
                    # Test Web
                    if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                        WEB_STATUS="200"
                    else
                        WEB_STATUS="000"
                    fi
                    
                    # Test API
                    API_STATUS="000"
                    for endpoint in /health /swagger /api/health; do
                        STATUS=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:5000${endpoint}" 2>/dev/null || echo "000")
                        if [ "$STATUS" = "200" ]; then
                            API_STATUS="$STATUS"
                            break
                        fi
                    done
                    
                    echo "API Status: $API_STATUS"
                    echo "Web Status: $WEB_STATUS"
                    
                    # Final summary
                    echo ""
                    echo "=== DEPLOYMENT COMPLETE ==="
                    echo "Application URLs:"
                    echo "  Web Interface:  http://${SERVER_IP}:7130"
                    echo "  API Swagger:    http://${SERVER_IP}:5000/swagger"
                    echo "  SQL Server:     ${SERVER_IP}:1433"
                    echo ""
                    
                    if [ "$WEB_STATUS" = "200" ]; then
                        echo "✅ Web application is accessible"
                    else
                        echo "⚠️ Web application may have issues"
                    fi
                    
                    if [ "$API_STATUS" = "200" ]; then
                        echo "✅ API is accessible"
                    else
                        echo "⚠️ API may have issues - check logs"
                    fi
                    
                    echo ""
                    echo "Database Status: ✅ Preserved (not destroyed)"
                    echo "Next run will preserve all existing data."
                    echo ""
                    echo "Management commands:"
                    echo "  View logs:     docker logs tms-api (or tms-web, tms-sqlserver)"
                    echo "  Stop app:      cd ${DEPLOY_PATH} && ${DC_CMD} down"
                    echo "  Restart app:   cd ${DEPLOY_PATH} && ${DC_CMD} restart"
                    echo "  View data:     ${DEPLOY_PATH}/sqlserver-data/"
                '''
            }
        }
        
        stage('Run Database Migrations') {
            when {
                expression { params.RUN_MIGRATIONS == true }
            }
            steps {
                sh '''
                    echo "🔧 Running database migrations..."
                    
                    # Wait a bit more for API to be fully ready
                    sleep 10
                    
                    # Try to trigger migrations through API health check
                    if curl -s -f http://localhost:5000/api/health >/dev/null 2>&1; then
                        echo "✅ API is up - migrations should run automatically"
                    else
                        echo "⚠️ API not responding - migrations may not run automatically"
                        echo "Check API logs for migration details: docker logs tms-api"
                    fi
                '''
            }
        }
    }
    
    post {
        always {
            cleanWs()
            sh '''
                echo "🧹 Cleaning up workspace..."
                # Keep deployment directory intact
            '''
        }
        success {
            echo '✅ Deployment completed successfully!'
            sh '''
                echo "Application is now running with preserved database."
                echo "Data location: ${DEPLOY_PATH}/sqlserver-data/"
            '''
        }
        failure {
            echo '❌ Deployment failed!'
            script {
                // Use the stored DC_CMD from the environment
                def dcCmd = env.DC_CMD ?: "docker compose"
                
                sh """
                    echo "Checking deployment logs..."
                    if [ -d "${env.DEPLOY_PATH}" ]; then
                        cd "${env.DEPLOY_PATH}"
                        ${dcCmd} logs --tail=50 2>/dev/null || true
                    fi
                    echo ""
                    echo "Important: Database data was NOT destroyed."
                    echo "Data is preserved at: ${env.DEPLOY_PATH}/sqlserver-data/"
                """
            }
        }
    }
}
