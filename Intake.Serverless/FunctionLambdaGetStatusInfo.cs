using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.S3;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Serilog;
using Intake.Common;
using Intake.Common.Model;
using System.Text.Json;
using System.IO;
using System.Text;

namespace Intake.Serverless
{
    // this class contains an Lambda Function acting as an API Endpoint via API Gateway with code to read a file from an S3 Bucket

    public class FunctionLambdaGetStatusInfo
    {
        private readonly IServerlessLogger serverlessLogger;
        private readonly string s3StatusBucketName;
        private readonly IMainStatusManager mainStatusManager;
        protected MainSettings MainSettingsInstance;

        public FunctionLambdaGetStatusInfo()
        {
            var configuration = new ConfigurationService().GetConfiguration();
            var awsOptions = configuration.GetSection(AwsOptions.SectionName).Get<AwsOptions>();
            s3StatusBucketName = awsOptions.CrvStatusBucketName;
            serverlessLogger = new ServerlessLogger(configuration[Constants.AWS_LogGroup]);
            mainStatusManager = new MainStatusManager(awsOptions);
        }

#pragma warning disable IDE0060 // Remove unused parameter
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            try
            {
                MainSettingsInstance = await mainStatusManager.GetStatus();
                serverlessLogger.LoadLogger(MainSettings.MapLogLevel(MainSettingsInstance.Level), GetType().Name);
                Log.Debug($"GetStatusInfo Entered");
                var domainName = request.RequestContext.DomainName;
                var stage = request.RequestContext.Stage;
                var endpoint = $"https://{domainName}/{stage}";
                Log.Debug($"API Gateway managment endpoint: {endpoint}");
                var connectionId = request.RequestContext.ConnectionId;
                Log.Debug($"ConnectionId: {connectionId}");
                var msg = "Connected.";
                request.RequestContext.Authorizer.TryGetValue("UserId", out var userId);
                var user = userId?.ToString();
                if (user is null || user == string.Empty)
                {
                    Log.Error("Unauthenticated access detected");
                    throw new Exception("Unknown user");
                }
                var apiClient = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
                {
                    ServiceURL = endpoint
                });
                var result = new GetStatusInfoNotification
                {
                    Action = "getstatusinfo",
                    Settings = MainSettingsInstance
                };
                var data = JsonSerializer.Serialize(result);
                var stream = new MemoryStream(UTF8Encoding.UTF8.GetBytes(data));
                var postConnectionRequest = new PostToConnectionRequest
                {
                    ConnectionId = connectionId,
                    Data = stream
                };
                Log.Debug($"Ready to send answer");
                var response =  await apiClient.PostToConnectionAsync(postConnectionRequest);
                Log.Debug(response.ToString());
                Log.Debug($"After send answer");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = msg
                };
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
    public sealed class GetStatusInfoNotification
    {

        //--- Constructors ---
        public GetStatusInfoNotification() => Action = "getstatusinfo";

        //--- Properties ---
        public string Action { get; set; }
        public MainSettings Settings { get; set; }
    }
}
