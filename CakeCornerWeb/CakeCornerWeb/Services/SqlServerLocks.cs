using CakeCornerWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Services;

public static class SqlServerLocks
{
    public static Task AcquireAsync(ApplicationDbContext context, string resource) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource},
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51000, 'Operation is busy. Please retry.', 1;
            """);
}
