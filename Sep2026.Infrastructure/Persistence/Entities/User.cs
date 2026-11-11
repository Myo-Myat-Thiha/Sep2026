using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class User
{
    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    public virtual ICollection<Portfolio> Portfolios { get; set; } = new List<Portfolio>();
}
