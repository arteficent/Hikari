namespace SyncServer.Identity.Models
{
    public enum Role
    {
        User,
        Admin,
        // Root is the singleton super-admin: the bootstrap-seeded account.
        // Only a Root user can list users, assign roles, delete users, and
        // create Admin accounts. Root cannot be assigned via the role API,
        // and a Root user cannot be demoted or deleted.
        Root
    }
}
