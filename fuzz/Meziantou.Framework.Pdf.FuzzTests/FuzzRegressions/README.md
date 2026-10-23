# Fuzz regressions

The inputs a fuzz campaign found failing, minimized, committed once fixed: `PdfFuzzRegressionTests` reads every file of
this folder (other than this README) with the oracle of the mutation fuzz tests. Name a file after the target and the
issue it fixes (`document-<issue>.pdf`, `filter-<filter>-<issue>.bin`).
