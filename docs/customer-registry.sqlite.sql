-- Run only against a new disposable local SQLite file, using your SQLite client.
CREATE TABLE Customers (
    Id TEXT PRIMARY KEY NOT NULL,
    DisplayName TEXT NOT NULL
);
INSERT INTO Customers (Id, DisplayName) VALUES ('CUST-001', 'SQLite Example Customer');
