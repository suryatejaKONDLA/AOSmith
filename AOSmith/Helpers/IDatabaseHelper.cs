using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace AOSmith.Helpers
{
    public interface IDatabaseHelper
    {
        // Stored Procedure Execution
        Task<DbReturnResult> ExecuteStoredProcedureWithOutputsAsync(string spName, List<SqlParameter> parameters);
        Task<int> ExecuteNonQueryAsync(string sqlQuery, Dictionary<string, object> parameters = null);

        // Query Execution (returns data)
        Task<List<T>> QueryAsync<T>(string sqlQuery, Dictionary<string, object> parameters = null) where T : new();
        Task<T> QuerySingleAsync<T>(string sqlQuery, Dictionary<string, object> parameters = null) where T : new();
    }
}
