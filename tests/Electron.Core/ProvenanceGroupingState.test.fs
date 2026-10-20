module ElectronCore.ProvenanceGroupingStateTests

open Swate.Components.Shared.ProvenanceGrouping.Fixtures
open Swate.Components.Shared.ProvenanceGrouping.Session
open Swate.Components.Shared.ProvenanceGrouping.Types
open Swate.Components.Page.ProvenanceGrouping
open Vitest

// Deactivated for now: ProvenanceGrouping is outdated.
Vitest.describe (
    "ProvenanceGrouping State",
    TestOptions(skip = true),
    fun () ->
        Vitest.test (
            "Sides.ensure returns the same instance for unchanged state",
            fun () ->
                let session = sampleSession ()
                let state = State.Sides.ensure session (State.init session)
                let ensured = State.Sides.ensure session state

                Vitest.expect(obj.ReferenceEquals(ensured, state)).toBe (true)
        )

        Vitest.test (
            "Sides.ensure still prunes pair-scoped state for removed pairs",
            fun () ->
                let session = sampleSession ()
                let pair = Session.activePair session

                let property =
                    pair.Model.PropertyValues
                    |> Map.toList
                    |> List.head
                    |> snd
                    |> ProvenancePropertyValue.propertyKey

                let staleSlot =
                    "removed-pair", ProvenanceSide.Input, State.Keys.groupingKey property

                let state = {
                    State.Sides.ensure session (State.init session) with
                        ExpandedProperties = Set.singleton staleSlot
                }

                let ensured = State.Sides.ensure session state

                Vitest.expect(obj.ReferenceEquals(ensured, state)).toBe (false)
                Vitest.expect(ensured.ExpandedProperties.Count).toBe (0)
        )
)
