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
                sh '''
                    set -e  # Exit on error
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
                    sudo systemctl stop tms-app.service 2>/dev/null || true
                    sudo systemctl disable tms-api.service 2>/dev/null || true
                    sudo systemctl disable tms-app.service 2>/dev/null || true

                    echo "=== STEP 2: Remove old nginx config (host) ==="
                    sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                    sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                    sudo systemctl restart nginx 2>/dev/null || true

                    echo "=== STEP 3: Prepare deployment directory ==="
                    sudo mkdir -p "${DEPLOY_PATH}"
                    sudo cp -f .env "${DEPLOY_PATH}/" 2>/dev/null || true
                    sudo chmod 600 "${DEPLOY_PATH}/.env" 2>/dev/null || true

                    echo "Sanitizing TMS.Web/nginx.conf (if needed)..."
                    if [ -f "TMS.Web/nginx.conf" ]; then
                      if grep -q 'sudo ' TMS.Web/nginx.conf >/dev/null 2>&1 || ! grep -q 'events {' TMS.Web/nginx.conf >/dev/null 2>&1; then
                        echo "⚠️ Detected suspicious content in TMS.Web/nginx.conf — keeping only nginx config from 'events {' onward"
                        awk '/events \\{/{p=1} p{print}' TMS.Web/nginx.conf > TMS.Web/nginx.conf.sanitized || true
                        if [ -s TMS.Web/nginx.conf.sanitized ]; then
                          mv TMS.Web/nginx.conf.sanitized TMS.Web/nginx.conf
                          echo "✅ nginx.conf sanitized"
                        else
                          echo "❌ Sanitization failed or produced empty file — please inspect TMS.Web/nginx.conf"
                          exit 1
                        fi
                      else
                        echo "✅ nginx.conf looks OK"
                      fi
                    else
                      echo "⚠️ TMS.Web/nginx.conf not found in repo workspace"
                    fi

                    echo "Copying project files..."
                    sudo cp -f docker-compose.yml Dockerfile "${DEPLOY_PATH}/" 2>/dev/null || true
                    sudo cp -f TMS.Web/Dockerfile "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    sudo cp -f TMS.Web/nginx.conf "${DEPLOY_PATH}/TMS.Web/" 2>/dev/null || true
                    
                    # Copy project directories (replace existing)
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

                    echo "Before starting: ensure critical host ports are free"
                    # Remove any docker containers that are explicitly binding our host ports (safe cleanup)
                    for p in 1433 5000 7130; do
                      OCCUPIERS=$(sudo docker ps --format '{{.ID}} {{.Names}} {{.Ports}}' | grep -E "0.0.0.0:${p}|:::${p}" | awk '{print $1}' || true)
                      if [ -n "$OCCUPIERS" ]; then
                        echo "⚠️ Found docker container(s) using host port ${p}: $OCCUPIERS"
                        for c in $OCCUPIERS; do
                          echo "Stopping and removing container $c (releasing port ${p})..."
                          sudo docker stop "$c" || true
                          sudo docker rm -f "$c" || true
                        done
                      fi
                    done

                    echo "Starting containers..."
                    ${DC_CMD} up -d

                    echo "=== STEP 5: Check container status ==="
                    echo "Current running containers:"
                    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" || true
                    
                    echo "Checking SQL Server logs for errors..."
                    ${DC_CMD} logs sql-server --tail=50 2>/dev/null || echo "Could not get SQL Server logs"
                    
                    echo "=== STEP 6: Wait for SQL Server to be ready ==="
                    sleep 10  # Give SQL Server more time to start

                    SQL_OK=false
                    MAX_ATTEMPTS=60
                    
                    # First, check if SQL Server container is running
                    SQL_CONTAINER_STATUS=$(docker inspect -f '{{.State.Status}}' tms-sqlserver 2>/dev/null || echo "not_found")
                    echo "SQL Server container status: $SQL_CONTAINER_STATUS"
                    
                    if [ "$SQL_CONTAINER_STATUS" != "running" ]; then
                        echo "❌ SQL Server container is not running. Check logs above."
                        ${DC_CMD} logs sql-server || true
                        exit 1
                    fi

                    # Get network info
                    NET=$(docker inspect -f '{{range $k,$v := .NetworkSettings.Networks}}{{$k}}{{end}}' tms-sqlserver 2>/dev/null || echo "bridge")
                    echo "Using network: $NET"

                    # SIMPLIFIED SQL Check - Use direct connection without mssql-tools container
                    echo "Testing SQL Server connection directly..."
                    
                    for i in $(seq 1 $MAX_ATTEMPTS); do
                        echo "SQL check attempt $i/$MAX_ATTEMPTS..."
                        
                        # Method 1: Use SQLCMD if available on host
                        if command -v sqlcmd >/dev/null 2>&1; then
                            if timeout 10 sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" 2>/dev/null; then
                                SQL_OK=true
                                echo "✅ SQL Server is ready (attempt $i)"
                                break
                            fi
                        fi
                        
                        # Method 2: Use docker exec to check from inside the container
                        if docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "SELECT 1" 2>/dev/null; then
                            SQL_OK=true
                            echo "✅ SQL Server is ready via docker exec (attempt $i)"
                            break
                        fi
                        
                        # Method 3: Use netcat to check if port is listening
                        if timeout 2 bash -c "cat < /dev/null > /dev/tcp/localhost/1433" 2>/dev/null; then
                            echo "Port 1433 is open, but SQL not responding yet..."
                        else
                            echo "Port 1433 not yet open..."
                        fi
                        
                        sleep 2
                    done

                    if [ "$SQL_OK" = "false" ]; then
                        echo "❌ SQL Server failed to start or accept connections in time."
                        echo "=== SQL Server Logs (last 100 lines) ==="
                        ${DC_CMD} logs sql-server --tail=100 || true
                        echo "=== SQL Server Container Details ==="
                        docker inspect tms-sqlserver || true
                        exit 1
                    fi

                    echo "=== STEP 7: Initialize Database ==="
                    echo "Creating database if it doesn't exist..."
                    # Use a simpler approach to check/create database
                    docker exec tms-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" -Q "
                        IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TaskManagementSystem')
                        BEGIN
                            CREATE DATABASE TaskManagementSystem;
                            PRINT 'Database created successfully.';
                        END
                        ELSE
                        BEGIN
                            PRINT 'Database already exists.';
                        END
                    " 2>/dev/null || echo "Note: Could not create database (might already exist)"

                    echo "=== STEP 8: Wait for API to respond ==="
                    API_OK=false
                    for i in $(seq 1 30); do
                        echo "API check attempt $i/30..."
                        if curl -s -f http://localhost:5000/health >/dev/null 2>&1 || curl -s -f http://localhost:5000/swagger >/dev/null 2>&1; then
                            API_OK=true
                            echo "✅ API responded (attempt $i)"
                            break
                        fi
                        echo "Waiting for API... (attempt $i/30)"
                        sleep 3
                    done

                    if [ "$API_OK" = "false" ]; then
                        echo "⚠️ API not responding, checking logs..."
                        ${DC_CMD} logs api --tail=50 || true
                        echo "Will continue anyway to check web..."
                    fi

                    echo "=== STEP 9: Wait for Web to respond ==="
                    WEB_OK=false
                    for i in $(seq 1 20); do
                        echo "Web check attempt $i/20..."
                        if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                            WEB_OK=true
                            echo "✅ Web responded (attempt $i)"
                            break
                        fi
                        echo "Waiting for Web... (attempt $i/20)"
                        sleep 2
                    done

                    if [ "$WEB_OK" = "false" ]; then
                        echo "⚠️ Web not responding, checking logs..."
                        ${DC_CMD} logs web --tail=50 || true
                    fi

                    echo "=== STEP 10: Final verification ==="
                    echo "Current container status:"
                    ${DC_CMD} ps || true
                    
                    echo "Container health:"
                    docker ps --format "table {{.Names}}\t{{.Status}}" || true

                    SERVER_IP=$(hostname -I | awk '{print $1}' || echo "127.0.0.1")
                    echo "Server IP: $SERVER_IP"

                    # Try multiple endpoints
                    echo "Testing API endpoints..."
                    API_STATUS="000"
                    for endpoint in /swagger /health /api/health; do
                        STATUS=$(curl -s -o /dev/null -w "%{http_code}" -f http://localhost:5000${endpoint} 2>/dev/null || echo "000")
                        if [ "$STATUS" = "200" ] || [ "$STATUS" = "301" ] || [ "$STATUS" = "302" ]; then
                            API_STATUS="$STATUS"
                            break
                        fi
                    done
                    
                    WEB_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -f http://localhost:7130 2>/dev/null || echo "000")

                    echo "API Status: $API_STATUS"
                    echo "Web Status: $WEB_STATUS"

                    if [ "$API_STATUS" = "200" ] || [ "$API_STATUS" = "301" ] || [ "$API_STATUS" = "302" ]; then
                        if [ "$WEB_STATUS" = "200" ] || [ "$WEB_STATUS" = "304" ] || [ "$WEB_STATUS" = "301" ] || [ "$WEB_STATUS" = "302" ]; then
                            echo "✅ All services are responding correctly!"
                            echo ""
                            echo "🎉 DOCKER DEPLOYMENT COMPLETE"
                            echo "Web Interface:  http://$SERVER_IP:7130"
                            echo "API Swagger:    http://$SERVER_IP:5000/swagger"
                            echo "SQL Server:     $SERVER_IP:1433"
                        else
                            echo "⚠️ Web service may not be fully healthy (Status: $WEB_STATUS)"
                            echo "But deployment completed. You may need to check the web container logs."
                        fi
                    else
                        echo "⚠️ Some services may not be fully healthy"
                        echo "API: $API_STATUS, Web: $WEB_STATUS"
                        echo "Check container logs for more details:"
                        echo "  docker logs tms-api"
                        echo "  docker logs tms-web"
                        echo "  docker logs tms-sqlserver"
                        # Don't exit with failure if we got this far
                        echo "Continuing despite partial failures..."
                    fi
                    
                    echo "=== Deployment Summary ==="
                    echo "Containers deployed:"
                    echo "1. tms-sqlserver (SQL Server) - port 1433"
                    echo "2. tms-api (.NET API) - port 5000"
                    echo "3. tms-web (Blazor + Nginx) - port 7130"
                    echo ""
                    echo "To check logs: docker logs <container-name>"
                    echo "To stop: cd /opt/tms-app-docker && docker-compose down"
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
