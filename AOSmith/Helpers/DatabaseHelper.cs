using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace AOSmith.Helpers
{
    public class DatabaseHelper : IDatabaseHelper
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"]?.ConnectionString
                                                    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");

        private const int CommandTimeout = 30; // Default timeout

        #region Connection Management

        private SqlConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }

        #endregion

        #region Stored Procedure Execution with Outputs

        public async Task<DbReturnResult> ExecuteStoredProcedureWithOutputsAsync(string spName, List<SqlParameter> parameters)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(spName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandTimeout = CommandTimeout;

                // Add input parameters
                if (parameters != null)
                {
                    command.Parameters.AddRange(parameters.ToArray());
                }

                // Add standard output parameters
                var resultValParam = new SqlParameter("@ResultVal", SqlDbType.Int) { Direction = ParameterDirection.Output };
                var resultTypeParam = new SqlParameter("@ResultType", SqlDbType.VarChar, 10) { Direction = ParameterDirection.Output };
                var resultMessageParam = new SqlParameter("@ResultMessage", SqlDbType.VarChar, -1) { Direction = ParameterDirection.Output };

                command.Parameters.Add(resultValParam);
                command.Parameters.Add(resultTypeParam);
                command.Parameters.Add(resultMessageParam);

                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();

                return new DbReturnResult
                {
                    ResultVal = resultValParam.Value != DBNull.Value ? Convert.ToInt32(resultValParam.Value) : -1,
                    ResultType = resultTypeParam.Value?.ToString() ?? "error",
                    ResultMessage = resultMessageParam.Value?.ToString() ?? "Unknown error occurred."
                };
            }
        }

        #endregion

        #region Execute Non-Query (INSERT/UPDATE/DELETE)

        public async Task<int> ExecuteNonQueryAsync(string sqlQuery, Dictionary<string, object> parameters = null)
        {
            using (var connection = CreateConnection())
            using (var command = new SqlCommand(sqlQuery, connection))
            {
                command.CommandType = CommandType.Text;
                command.CommandTimeout = CommandTimeout;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                await connection.OpenAsync();
                return await command.ExecuteNonQueryAsync();
            }
        }

        #endregion

        #region Query Methods (Dapper)

        public async Task<List<T>> QueryAsync<T>(string sqlQuery, Dictionary<string, object> parameters = null) where T : new()
        {
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync();
                var result = await connection.QueryAsync<T>(sqlQuery, parameters != null ? DictionaryToObject(parameters) : null);
                return result.ToList();
            }
        }

        public async Task<T> QuerySingleAsync<T>(string sqlQuery, Dictionary<string, object> parameters = null) where T : new()
        {
            using (var connection = CreateConnection())
            {
                await connection.OpenAsync();
                return await connection.QueryFirstOrDefaultAsync<T>(sqlQuery, parameters != null ? DictionaryToObject(parameters) : null);
            }
        }

        private static DynamicParameters DictionaryToObject(Dictionary<string, object> dict)
        {
            var dp = new DynamicParameters();
            foreach (var kvp in dict)
            {
                dp.Add(kvp.Key, kvp.Value ?? DBNull.Value);
            }
            return dp;
        }

        #endregion
    }
}
