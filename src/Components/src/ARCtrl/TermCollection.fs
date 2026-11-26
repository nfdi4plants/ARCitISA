module ARCtrl.TermCollection

/// <summary>
/// https://github.com/nfdi4plants/nfdi4plants_ontology/issues/85
/// </summary>
let Published = OntologyAnnotation("published", "EFO", "EFO:0001796")

/// <summary>
/// https://github.com/nfdi4plants/ARCitISA/issues/409#issuecomment-2176134201
/// </summary>
let PublicationStatus =
    OntologyAnnotation("publication status", "EFO", "EFO:0001742")

/// <summary>
/// https://github.com/nfdi4plants/ARCitISA/issues/409#issuecomment-2176134201
/// </summary>
let PersonRoleWithinExperiment =
    OntologyAnnotation("person role within the experiment", "AGRO", "AGRO:00000378")

/// <summary>
/// https://github.com/nfdi4plants/ARCitISA/issues/483#issuecomment-2228372546
/// </summary>
let Unit = OntologyAnnotation("unit", "UO", "UO:0000000")

/// <summary>
/// !! THIS IS NORMALLY `Data Type`
/// https://github.com/nfdi4plants/ARCitISA/issues/483#issuecomment-2228372546
/// </summary>
let ObjectType = OntologyAnnotation("Object Type", "NCIT", "NCIT:C42645")

/// <summary>
/// https://github.com/nfdi4plants/ARCitISA/issues/483#issuecomment-2260176815
/// </summary>/// <summary>
let Explication = OntologyAnnotation("Explication", "DPBO", "DPBO:0000111")
