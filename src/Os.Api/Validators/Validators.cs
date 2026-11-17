using FluentValidation;
using Os.Application.DTOs;

namespace Os.Api.Validators;

public class CriarClienteValidator : AbstractValidator<CriarClienteRequest>
{
    public CriarClienteValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Documento).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Telefone).NotEmpty().MaximumLength(30);
    }
}

public class CadastrarVeiculoValidator : AbstractValidator<CadastrarVeiculoRequest>
{
    public CadastrarVeiculoValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.Placa).NotEmpty();
        RuleFor(x => x.Marca).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Modelo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Ano).GreaterThan(1900).LessThanOrEqualTo(DateTime.UtcNow.Year + 1);
    }
}

public class AbrirOSValidator : AbstractValidator<AbrirOSRequest>
{
    public AbrirOSValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.VeiculoId).NotEmpty();
    }
}

public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Senha).NotEmpty().MinimumLength(8);
    }
}

public class CriarUsuarioValidator : AbstractValidator<CriarUsuarioRequest>
{
    public CriarUsuarioValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Senha).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public class DefinirItensRequestValidator : AbstractValidator<DefinirItensRequest>
{
    public DefinirItensRequestValidator()
    {
        RuleForEach(x => x.Servicos).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.Id).NotEmpty();
            i.RuleFor(x => x.Quantidade).GreaterThan(0);
        }).When(x => x.Servicos is not null);
        RuleForEach(x => x.Pecas).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.Id).NotEmpty();
            i.RuleFor(x => x.Quantidade).GreaterThan(0);
        }).When(x => x.Pecas is not null);
    }
}