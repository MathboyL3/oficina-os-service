using FluentAssertions;
using Os.Domain.Common;
using Os.Domain.Identidade;

namespace Os.Tests.Domain.Identidade;

public class UsuarioTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveCriar()
    {
        var u = Usuario.Criar("Admin", "admin@oficina.com", "hash$bcrypt", Role.Gerente);
        u.Nome.Should().Be("Admin");
        u.Email.Should().Be("admin@oficina.com");
        u.Role.Should().Be(Role.Gerente);
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("")]
    public void Criar_ComEmailInvalido_DeveLancar(string email)
    {
        Action act = () => Usuario.Criar("X", email, "hash", Role.Atendente);
        act.Should().Throw<DomainException>();
    }
}
