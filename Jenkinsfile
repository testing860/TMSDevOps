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
        withEnv([
            "DB_PASSWORD=${DB_PASSWORD}",
            "ADMIN_PASSWORD=${ADMIN_PASSWORD}",
            "JWT_KEY=${JWT_KEY}"
        ]) {
            sh '''
                #!/bin/bash
                set -euo pipefail

                echo "🚀 Starting local Docker deployment"

                cd "$WORKSPACE"

                echo "=== Checking Docker installation ==="
                if ! command -v docker >/dev/null 2>&1; then
                    echo "❌ Docker is not installed!"
                    exit 1
                fi
                if ! command -v docker-compose >/dev/null 2>&1 && ! docker compose version >/dev/null 2>&1; then
                    echo "❌ Docker Compose is not installed!"
                    exit 1
                fi
                echo "✅ Docker: $(docker --version)"
                echo "✅ Docker Compose: $(docker-compose --version 2>/dev/null || docker compose version)"

                echo "=== STEP 1: Stop old services ==="
                sudo systemctl stop tms-api.service 2>/dev/null || true
                sudo systemctl disable tms-api.service 2>/dev/null || true

                echo "=== STEP 2: Remove old nginx config ==="
                sudo rm -f /etc/nginx/sites-enabled/tms 2>/dev/null || true
                sudo rm -f /etc/nginx/sites-available/tms 2>/dev/null || true
                sudo systemctl restart nginx 2>/dev/null || true

                echo "=== STEP 3: Prepare deployment directory ==="
                sudo mkdir -p "${DEPLOY_PATH}"
                sudo cp -f .env "${DEPLOY_PATH}/"
                sudo chmod 600 "${DEPLOY_PATH}/.env"

                echo "Copying project files..."
                find . -maxdepth 1 \( -name "*.yml" -o -name "Dockerfile*" -o -name "*.sln" \) -type f -print0 | xargs -0 -r sudo cp -t "${DEPLOY_PATH}" || true
                sudo cp -r TMS.API TMS.Web TMS.Shared "${DEPLOY_PATH}/"
                sudo chown -R jenkins:jenkins "${DEPLOY_PATH}/"
                echo "✅ Files copied"

                echo "=== STEP 4: Build and start Docker containers ==="
                cd "${DEPLOY_PATH}"
                sudo docker-compose down --remove-orphans || true
                sudo docker-compose build --no-cache
                sudo docker-compose up -d

                echo "=== STEP 5: Wait for SQL Server ==="
                sleep 5
                for i in {1..30}; do
                    if sudo docker-compose exec -T sql-server /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$DB_PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; then
                        echo "✅ SQL Server ready"
                        break
                    fi
                    echo "Waiting for SQL Server ($i/30)..."
                    sleep 2
                done

                echo "=== STEP 6: Wait for API ==="
                for i in {1..20}; do
                    if curl -s -f http://localhost:5000/swagger >/dev/null 2>&1; then
                        echo "✅ API ready"
                        break
                    fi
                    sleep 3
                done

                echo "=== STEP 7: Wait for Web ==="
                for i in {1..15}; do
                    if curl -s -f http://localhost:7130 >/dev/null 2>&1; then
                        echo "✅ Web ready"
                        break
                    fi
                    sleep 2
                done

                echo "🎉 Deployment complete"
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
