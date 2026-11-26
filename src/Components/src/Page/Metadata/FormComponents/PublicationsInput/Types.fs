module Arcitisa.Components.Page.Metadata.FormComponents.PublicationsInput.Types

type SearchResult = {
    key: string
    title: string option
    authors: string option
    doi: string option
    pubMedId: string option
    pmcId: string option
    journal: string option
    year: string option
}

type SearchPage = {
    results: SearchResult[]
    nextCursor: string option
}

/// Optional fields reflect metadata that may be absent from provider responses.
type CrossrefAuthor =
    abstract given: string option
    abstract family: string option
    abstract name: string option

type CrossrefDate =
    abstract ``date-parts``: int option[][] option

type CrossrefWork =
    abstract DOI: string option
    abstract title: string[] option
    abstract author: CrossrefAuthor[] option
    abstract ``container-title``: string[] option
    abstract published: CrossrefDate option
    abstract ``published-print``: CrossrefDate option
    abstract ``published-online``: CrossrefDate option
    abstract issued: CrossrefDate option

type CrossrefResponse =
    abstract message: CrossrefWork option

type EuropePmcResult =
    abstract id: string option
    abstract source: string option
    abstract pmid: string option
    abstract pmcid: string option
    abstract doi: string option
    abstract title: string option
    abstract authorString: string option
    abstract journalTitle: string option
    abstract pubYear: string option

type EuropePmcResultList =
    abstract result: EuropePmcResult[] option

type EuropePmcResponse =
    abstract resultList: EuropePmcResultList option
    abstract nextCursorMark: string option
