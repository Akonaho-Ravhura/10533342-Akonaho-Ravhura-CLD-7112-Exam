using _10533342_AgriStreamIOT_Akonaho_Ravhura.Models;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Identity;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// ASP.NET Core Identity user store on Azure Table Storage.
    /// Users table: PartitionKey "user", RowKey = user id.
    /// UserRoles table: PartitionKey = user id, RowKey = normalised role name (e.g. "FARMER").
    /// </summary>
    public class TableUserStore :
        IUserPasswordStore<ApplicationUser>,
        IUserEmailStore<ApplicationUser>,
        IUserSecurityStampStore<ApplicationUser>,
        IUserRoleStore<ApplicationUser>,
        IUserLockoutStore<ApplicationUser>
    {
        private const string UserPartition = "user";
        private readonly AgriDataStore _store;

        public TableUserStore(AgriDataStore store) => _store = store;

        private TableClient Users => _store.Table(TableNames.Users);
        private TableClient UserRoles => _store.Table(TableNames.UserRoles);

        // ---------- IUserStore ----------

        public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
        {
            await Users.AddEntityAsync(TableMapper.ToEntity(user, UserPartition, user.Id), ct);
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
        {
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            await Users.UpsertEntityAsync(TableMapper.ToEntity(user, UserPartition, user.Id), TableUpdateMode.Replace, ct);
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
        {
            await foreach (var role in UserRoles.QueryAsync<TableEntity>(
                               TableClient.CreateQueryFilter($"PartitionKey eq {user.Id}"), cancellationToken: ct))
                await UserRoles.DeleteEntityAsync(role.PartitionKey, role.RowKey, cancellationToken: ct);
            await Users.DeleteEntityAsync(UserPartition, user.Id, cancellationToken: ct);
            return IdentityResult.Success;
        }

        public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct)
        {
            var result = await Users.GetEntityIfExistsAsync<TableEntity>(UserPartition, userId, cancellationToken: ct);
            return result.HasValue ? TableMapper.FromEntity<ApplicationUser>(result.Value!) : null;
        }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) =>
            FindOneAsync(TableClient.CreateQueryFilter($"PartitionKey eq {UserPartition} and NormalizedUserName eq {normalizedUserName}"), ct);

        private async Task<ApplicationUser?> FindOneAsync(string filter, CancellationToken ct)
        {
            await foreach (var entity in Users.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct))
                return TableMapper.FromEntity<ApplicationUser>(entity);
            return null;
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) { user.UserName = userName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }

        // ---------- Password ----------

        public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken ct) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash != null);

        // ---------- Email ----------

        public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken ct) { user.Email = email; return Task.CompletedTask; }
        public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Email);
        public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.EmailConfirmed);
        public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
        public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedEmail);
        public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken ct) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }

        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
            FindOneAsync(TableClient.CreateQueryFilter($"PartitionKey eq {UserPartition} and NormalizedEmail eq {normalizedEmail}"), ct);

        // ---------- Security stamp ----------

        public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
        public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.SecurityStamp);

        // ---------- Roles ----------

        public async Task AddToRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct)
        {
            var role = await new TableRoleStore(_store).FindByNameAsync(normalizedRoleName, ct)
                ?? throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");
            await UserRoles.UpsertEntityAsync(new TableEntity(user.Id, normalizedRoleName) { ["RoleName"] = role.Name }, cancellationToken: ct);
        }

        public async Task RemoveFromRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct)
        {
            try { await UserRoles.DeleteEntityAsync(user.Id, normalizedRoleName, cancellationToken: ct); }
            catch (RequestFailedException ex) when (ex.Status == 404) { }
        }

        public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct)
        {
            var roles = new List<string>();
            await foreach (var row in UserRoles.QueryAsync<TableEntity>(
                               TableClient.CreateQueryFilter($"PartitionKey eq {user.Id}"), cancellationToken: ct))
                roles.Add(row.GetString("RoleName") ?? row.RowKey);
            return roles;
        }

        public async Task<bool> IsInRoleAsync(ApplicationUser user, string normalizedRoleName, CancellationToken ct) =>
            (await UserRoles.GetEntityIfExistsAsync<TableEntity>(user.Id, normalizedRoleName, cancellationToken: ct)).HasValue;

        public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken ct)
        {
            var users = new List<ApplicationUser>();
            await foreach (var row in UserRoles.QueryAsync<TableEntity>(
                               TableClient.CreateQueryFilter($"RowKey eq {normalizedRoleName}"), cancellationToken: ct))
            {
                var user = await FindByIdAsync(row.PartitionKey, ct);
                if (user != null) users.Add(user);
            }
            return users;
        }

        // ---------- Lockout ----------

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
        public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken ct) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(++user.AccessFailedCount);
        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnabled);
        public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken ct) { user.LockoutEnabled = enabled; return Task.CompletedTask; }

        public void Dispose() { }
    }

    /// <summary>Identity role store on Azure Table Storage. Roles table: PartitionKey "role", RowKey = role id.</summary>
    public class TableRoleStore : IRoleStore<IdentityRole>
    {
        private const string RolePartition = "role";
        private readonly AgriDataStore _store;

        public TableRoleStore(AgriDataStore store) => _store = store;

        private TableClient Roles => _store.Table(TableNames.Roles);

        public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken ct)
        {
            await Roles.AddEntityAsync(TableMapper.ToEntity(role, RolePartition, role.Id), ct);
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken ct)
        {
            role.ConcurrencyStamp = Guid.NewGuid().ToString();
            await Roles.UpsertEntityAsync(TableMapper.ToEntity(role, RolePartition, role.Id), TableUpdateMode.Replace, ct);
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken ct)
        {
            await Roles.DeleteEntityAsync(RolePartition, role.Id, cancellationToken: ct);
            return IdentityResult.Success;
        }

        public async Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken ct)
        {
            var result = await Roles.GetEntityIfExistsAsync<TableEntity>(RolePartition, roleId, cancellationToken: ct);
            return result.HasValue ? TableMapper.FromEntity<IdentityRole>(result.Value!) : null;
        }

        public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct)
        {
            await foreach (var entity in Roles.QueryAsync<TableEntity>(
                               TableClient.CreateQueryFilter($"PartitionKey eq {RolePartition} and NormalizedName eq {normalizedRoleName}"),
                               maxPerPage: 1, cancellationToken: ct))
                return TableMapper.FromEntity<IdentityRole>(entity);
            return null;
        }

        public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.Id);
        public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.Name);
        public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken ct) { role.Name = roleName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken ct) => Task.FromResult(role.NormalizedName);
        public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken ct) { role.NormalizedName = normalizedName; return Task.CompletedTask; }

        public void Dispose() { }
    }
}
