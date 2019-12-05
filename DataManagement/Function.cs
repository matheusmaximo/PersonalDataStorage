using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace DataManagement
{
    public class Function
    {
        public const string TableNameVariableName = "TableName";

        private readonly Table _dataDynamoDbTable;

        public Function()
        {
            var _dynamoDbClient = new AmazonDynamoDBClient();

            string tableName = GetDataTableName();
            _dataDynamoDbTable = Table.LoadTable(_dynamoDbClient, tableName);
        }

        private string GetDataTableName()
        {
            var tableName = Environment.GetEnvironmentVariable(TableNameVariableName);
            return tableName;
        }

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> PutDataFunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                ExtractParameters(request, out string tenantId, out string patientId, out string caseId);

                var personalData = request.Body;
                var dataToBeSaved = new DataModel
                {
                    PatientId = $"{tenantId}#{patientId}",
                    CaseId = caseId,
                    ExpirationDate = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
                    DataAsJson = personalData
                };
                ValidateModel(dataToBeSaved);
                await SaveData(dataToBeSaved);

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.Accepted
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine(JsonConvert.SerializeObject(ex, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                }));
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"{ex.GetType().Name} - {ex.Message}"
                };
            }
        }

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> GetDataFunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                ExtractParameters(request, out string tenantId, out string patientId, out string caseId);
                context.Logger.LogLine($"{nameof(tenantId)}: {tenantId}");
                context.Logger.LogLine($"{nameof(patientId)}: {patientId}");
                context.Logger.LogLine($"{nameof(caseId)}: {caseId}");

                ValidateTenantId(tenantId);
                ValidatePatientId(patientId);

                var patientIdentifier = $"{tenantId}#{patientId}";
                string body;
                if (string.IsNullOrWhiteSpace(caseId))
                {
                    var data = await GetPatientData(patientIdentifier);
                    body = JsonConvert.SerializeObject(data);
                }
                else
                {
                    var dataModel = await GetDataModel(patientIdentifier, caseId);
                    body = dataModel.DataAsJson;
                }

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = body
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine(JsonConvert.SerializeObject(ex, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                }));
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"{ex.GetType().Name} - {ex.Message}"
                };
            }
        }

        private async Task<List<string>> GetPatientData(string patientIdentifier)
        {
            var config = new QueryOperationConfig();
            config.Filter = new QueryFilter();
            config.Filter.AddCondition(nameof(DataModel.PatientId), QueryOperator.Equal, patientIdentifier);
            config.AttributesToGet = new List<string> { nameof(DataModel.DataAsJson) };
            config.Select = SelectValues.SpecificAttributes;

            var dataDocuments = await _dataDynamoDbTable.Query(config).GetRemainingAsync();
            if (dataDocuments == null || !dataDocuments.Any())
            {
                throw new DataNotFoundException(patientIdentifier);
            }

            var values = dataDocuments.Select(dataDocument => GetValue(dataDocument[nameof(DataModel.DataAsJson)])).ToList();

            return values;
        }

        private async Task SaveData(DataModel dataToBeSaved)
        {
            await _dataDynamoDbTable.PutItemAsync(new Document
            {
                [nameof(DataModel.TenantId)] = new Primitive(dataToBeSaved.TenantId),
                [nameof(DataModel.PatientId)] = new Primitive(dataToBeSaved.PatientId),
                [nameof(DataModel.CaseId)] = new Primitive(dataToBeSaved.CaseId),
                [nameof(DataModel.ExpirationDate)] = new Primitive(dataToBeSaved.ExpirationDate.ToString(), true),
                [nameof(DataModel.DataAsJson)] = new Primitive(dataToBeSaved.DataAsJson)
            });
        }

        private async Task<DataModel> GetDataModel(string patientId, string caseId)
        {
            var dataDocument = await _dataDynamoDbTable.GetItemAsync(patientId, caseId);
            if (dataDocument == null)
            {
                throw new DataNotFoundException($"{patientId}#{caseId}");
            }
            var dataModel = new DataModel
            {
                PatientId = GetValue(dataDocument[nameof(DataModel.PatientId)]),
                CaseId = GetValue(dataDocument[nameof(DataModel.CaseId)]),
                ExpirationDate = long.Parse(GetValue(dataDocument[nameof(DataModel.ExpirationDate)])),
                DataAsJson = GetValue(dataDocument[nameof(DataModel.DataAsJson)])
            };
            return dataModel;
        }

        private static string GetValue(DynamoDBEntry attributeValue)
        {
            if (attributeValue is null)
            {
                return null;
            }

            var value = attributeValue.AsString();
            return value;
        }

        private void ExtractParameters(APIGatewayProxyRequest request, out string tenantId, out string patientId, out string caseId)
        {
            tenantId = request.PathParameters[nameof(tenantId)];
            patientId = request.PathParameters[nameof(patientId)];
            caseId = request.PathParameters[nameof(caseId)];
        }

        private void ValidateModel<T>(T dataToBeSaved) where T : class
        {
            if (dataToBeSaved is null)
            {
                throw new ArgumentNullException(nameof(dataToBeSaved));
            }

            var context = new ValidationContext(dataToBeSaved);
            Validator.ValidateObject(dataToBeSaved, context);
        }

        private void ValidateTenantId(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new MissingParameterException(nameof(tenantId));
            }
        }

        private void ValidatePatientId(string patientId)
        {
            if (string.IsNullOrWhiteSpace(patientId))
            {
                throw new MissingParameterException(nameof(patientId));
            }
        }
    }
}
