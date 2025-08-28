
using Api.Auth;

namespace Api.GraphQL;

public class AuthorizationQueries
{
    // GraphQL: canViewAuthEvents(userId: ID!): Boolean!
    public async Task<bool> CanViewAuthEventsAsync(
        Guid userId,
        [Service] AuthorizationService authorization)
    {
        return await authorization.CanViewAuthEventsAsync(userId);
    }

    // GraphQL: canViewRoleChanges(userId: ID!): Boolean!
    public async Task<bool> CanViewRoleChangesAsync(
        Guid userId,
        [Service] AuthorizationService authorization)
    {
        return await authorization.CanViewRoleChangesAsync(userId);
    }
}

public class Query()
{

}
