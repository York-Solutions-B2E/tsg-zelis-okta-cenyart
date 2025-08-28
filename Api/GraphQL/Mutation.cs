using Api.Auth;
using Api.Data;

namespace Api.GraphQL;

public class ProvisioningMutations
{
    // GraphQL: provisionOnLogin(externalId, email, provider) : ProvisionResult!
    public async Task<ProvisionResult> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        [Service] ProvisioningService provisioning,
        [Service] AppDbContext db) // used to eagerly return Role info
    {
        var user = await provisioning.ProvisionOnLoginAsync(externalId, email, provider);

        // ensure role navigation loaded
        await db.Entry(user).Reference(u => u.Role).LoadAsync();

        var userDto = new UserDto(user.Id, user.Email, new RoleDto(user.Role.Id, user.Role.Name));
        return new ProvisionResult(true, "Provisioned", userDto);
    }
}

public class Mutation()
{

}
