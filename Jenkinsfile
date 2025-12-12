pipeline {
    agent any
    
    environment {
        GITHUB_SSH_CREDENTIAL_ID = 'f2279fbb-b675-4191-bb6e-5e5c0d1421a5'
        DB_PASSWORD = credentials('tms-db-password')
        JWT_KEY = credentials('tms-jwt-key')
        ADMIN_PASSWORD = credentials('tms-admin-password')
        APP_URLS = credentials('tms-app-urls')
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
        
        stage('Create Secure Environment File') {
            steps {
                sh '''
                    cat > .env << EOF
DB_SERVER=localhost
DB_NAME=TaskManagementSystem
DB_USER=sa
DB_PASSWORD=''' + DB_PASSWORD + '''

JWT_KEY=''' + JWT_KEY + '''
JWT_ISSUER=TMSAPI
JWT_AUDIENCE=TMSWebClient

ADMIN_EMAIL=admin@tms.com
ADMIN_PASSWORD=''' + ADMIN_PASSWORD + '''
ADMIN_DISPLAYNAME=Admin

ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=''' + APP_URLS + '''
EOF
                    echo "✅ .env file created"
                '''
            }
        }
        
        stage('Publish & Deploy') {
            steps {
                sh '''
                    echo "Publishing API..."
                    dotnet publish TMS.API/TMS.API.csproj -c Release -o ./publish-api --runtime linux-x64
                    
                    echo "Publishing Web..."
                    dotnet publish TMS.Web/TMS.Web.csproj -c Release -o ./publish-web --runtime linux-x64
                    
                    echo "Deploying to /opt/tms-app/"
                    
                    sudo mkdir -p /opt/tms-app/api
                    sudo mkdir -p /opt/tms-app/web
                    
                    sudo systemctl stop tms-api.service 2>/dev/null || true
                    
                    sudo rm -rf /opt/tms-app/api/*
                    sudo cp -r ./publish-api/* /opt/tms-app/api/
                    sudo cp .env /opt/tms-app/api/
                    
                    sudo rm -rf /opt/tms-app/web/*
                    sudo cp -r ./publish-web/wwwroot/* /opt/tms-app/web/
                    
                    sudo chown -R ec:ec /opt/tms-app
                    
                    # Create API service
                    sudo tee /etc/systemd/system/tms-api.service << "API_SERVICE"
[Unit]
Description=TMS API Backend
After=network.target

[Service]
WorkingDirectory=/opt/tms-app/api
ExecStart=/usr/bin/dotnet TMS.API.dll
EnvironmentFile=/opt/tms-app/api/.env
Restart=always
RestartSec=10
User=ec
Group=ec
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
API_SERVICE
                    
                    # Create nginx config for port 7130
                    sudo tee /etc/nginx/sites-available/tms << "NGINX_CONFIG"
server {
    listen 7130;
    server_name _;
    root /opt/tms-app/web;
    
    location / {
        try_files $uri $uri/ /index.html;
    }
    
    location /api {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }
}
NGINX_CONFIG
                    
                    sudo ln -sf /etc/nginx/sites-available/tms /etc/nginx/sites-enabled/
                    sudo rm -f /etc/nginx/sites-enabled/default 2>/dev/null || true
                    sudo nginx -t
                    sudo systemctl restart nginx
                    
                    sudo systemctl daemon-reload
                    sudo systemctl enable tms-api.service
                    sudo systemctl start tms-api.service
                    
                    echo "🚀 Deployment complete!"
                    echo "Web: http://$(hostname -I | awk '{print $1}'):7130"
                    echo "API: http://$(hostname -I | awk '{print $1}'):5000"
                '''
            }
        }
    }
    
    post {
        failure {
            echo '❌ Pipeline failed'
        }
        success {
            echo '✅ Pipeline succeeded'
        }
    }
}
