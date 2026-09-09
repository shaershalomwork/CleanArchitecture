CREATE TABLE dbo.Customers (Id nvarchar(50) NOT NULL PRIMARY KEY, DisplayName nvarchar(200) NOT NULL);
INSERT dbo.Customers (Id, DisplayName) VALUES ('CUST-001', 'Fixture Customer');
GO
CREATE PROCEDURE dbo.GetCustomer @CustomerId nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, DisplayName FROM dbo.Customers WHERE Id = @CustomerId;
    IF @@ROWCOUNT = 0 RETURN 404;
    RETURN 0;
END;
