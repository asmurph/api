namespace EntraApi.Auth
{
    public static class AuthConstants
    {
        /// <summary>Delegated scope exposed by the API app registration in Entra ID.</summary>
        public const string RequiredScope = "access_as_user";

        /// <summary>App roles defined on the API app registration (assigned to users/groups).</summary>
        public static class Roles
        {
            public const string Reader = "Patients.Read";
            public const string Writer = "Patients.Write";
            public const string Admin = "Patients.Admin";
        }

        public static class Policies
        {
            public const string PatientsRead = "PatientsRead";
            public const string PatientsWrite = "PatientsWrite";
            public const string PatientsDelete = "PatientsDelete";
        }
    }
}
