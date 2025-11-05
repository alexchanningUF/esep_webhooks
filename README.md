# esep_webhooks

Lambda that handles GitHub **issues** webhooks and posts to Slack.

## Build & Deploy

# one-time:
dotnet tool install -g Amazon.Lambda.Tools
dotnet new -i Amazon.Lambda.Templates

# configure AWS creds (IAM user with IAMFullAccess + AWSLambda_FullAccess)
aws configure  # region: us-east-2, output: json

# restore/build
dotnet restore
dotnet build -c Release

# deploy
dotnet lambda deploy-function EsepWebhook