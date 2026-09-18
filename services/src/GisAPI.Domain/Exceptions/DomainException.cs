namespace GisAPI.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object key)
        : base($"Entity \"{entityName}\" ({key}) was not found.") { }

    // Message affiché tel quel par les écrans (err.error.message) : faute de ce
    // constructeur, quatre sous-classes existaient pour remplacer le gabarit anglais.
    public NotFoundException(string message) : base(message) { }
}

public class ForbiddenAccessException : DomainException
{
    public ForbiddenAccessException() : base("Access denied.") { }
    public ForbiddenAccessException(string message) : base(message) { }
}

public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}


