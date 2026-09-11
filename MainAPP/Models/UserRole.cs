namespace MainAPP.Models
{
    /// <summary>
    /// 用户角色枚举，控制不同角色的功能访问权限。
    /// Guest: 访客，仅可查看主页；
    /// Operator: 操作员，可访问主页和数据库；
    /// Admin: 管理员，可访问所有功能包括配方管理和系统设置。
    /// </summary>
    public enum UserRole
    {
        /// <summary>访客，最低权限</summary>
        Guest = 0,
        /// <summary>操作员，可查看数据和日志</summary>
        Operator = 1,
        /// <summary>管理员，完全权限</summary>
        Admin = 2
    }
}