using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GetObjectRequest = Amazon.S3.Model.GetObjectRequest;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Intake.Serverless
{
    // this class contains an S3 trigger Lambda Function code with Azure AD(Entra ID) authentication
    public class Function
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly IAmazonS3 _s3Client;

        public Function() : this(new AmazonS3Client()) { }

        public Function(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
        }

        public async Task<string> FunctionHandler(S3Event s3Event, ILambdaContext context)
        {
            if (s3Event?.Records == null || !s3Event.Records.Any())
                return "No records found in the event.";

            // Authenticate with Azure Entra ID before processing the file
            string accessToken = await GetAzureAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                context.Logger.LogError("Failed to acquire Azure Entra ID access token.");
                return "Authentication failed.";
            }
            context.Logger.LogInformation("Successfully authenticated with Azure Entra ID.");

            foreach (var record in s3Event.Records)
            {
                string bucketName = record.S3.Bucket.Name;
                string objectKey = record.S3.Object.Key;

                context.Logger.LogInformation($"Processing file {objectKey} from bucket {bucketName}.");

                // Read JSON file content from S3
                string fileContent = await ReadFileFromS3(bucketName, objectKey, context);
                if (fileContent == null)
                {
                    context.Logger.LogError("Failed to read the JSON file from S3.");
                    return "File read failed.";
                }

                context.Logger.LogInformation($"File content: {fileContent}");
            }

            return "Processing completed.";
        }

        private async Task<string> ReadFileFromS3(string bucketName, string objectKey, ILambdaContext context)
        {
            try
            {
                Amazon.S3.Model.GetObjectRequest request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                };

                using (GetObjectResponse response = await _s3Client.GetObjectAsync(request))
                using (var reader = new System.IO.StreamReader(response.ResponseStream, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error reading file from S3: {ex.Message}");
                return null;
            }
        }

        private async Task<string> GetAzureAccessToken()
        {
            var clientApp = ConfidentialClientApplicationBuilder.Create("Your-Client-Id")
                .WithClientSecret("Your-Client-Secret")
                .WithAuthority(new Uri("https://login.microsoftonline.com/Your-Tenant-ID"))
                .Build();

            var scopes = new string[] { "https://graph.microsoft.com/.default" };
            try
            {
                var result = await clientApp.AcquireTokenForClient(scopes).ExecuteAsync();
                return result.AccessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error acquiring token: {ex.Message}");
                return null;
            }
        }
    }
}
