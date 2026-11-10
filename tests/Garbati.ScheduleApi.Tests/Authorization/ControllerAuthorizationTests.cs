using System.Reflection;
using Garbati.ScheduleApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.ScheduleApi.Tests.Authorization;

// Guards new endpoints: every controller action must name exactly one known scope policy. Same test as
// every sibling service's ControllerAuthorizationTests.
public class ControllerAuthorizationTests
{
    [Fact]
    public void EveryControllerAction_RequiresAKnownScopePolicy()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName)
            .ToList();

        var violations = actions
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || m.GetCustomAttribute<AuthorizeAttribute>()?.Policy is not { } policy
                || !ScheduleApiScopes.All.Contains(policy))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(actions.Count >= 7, $"Expected at least 7 controller actions (one per endpoint), found {actions.Count}.");
        Assert.Empty(violations);
    }
}
