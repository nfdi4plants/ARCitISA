namespace Arcitisa.Components.Composite.Examples

open System
open Arcitisa.Components.Composite.Notes.Types
open ARCtrl

module noteSearchTests =

    let notes: Note list = [
        {
            RelativePath = "notes/2026-02-10/Grocery_Planning/Grocery_Planning.md"
            Title = "Grocery Planning"
            Date = DateTime(2026, 2, 10)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Planning", "http://example.com/ontology/planning")
                        OntologyAnnotation("Food", "http://example.com/ontology/food")
                        OntologyAnnotation("Weekly", "http://example.com/ontology/weekly")
                    ]
                )
            Content =
                "I need to prepare a proper grocery list for the week. We are running low on vegetables and fresh fruit. I also want to try cooking a new pasta recipe. Remember to check if we still have olive oil and spices. It might be worth buying extra rice in bulk. I should compare prices between the local store and the supermarket. Don't forget snacks for movie night."
        }
        {
            RelativePath = "notes/2026-02-12/Project_Ideas_for_Side_App/Project_Ideas_for_Side_App.md"
            Title = "Project Ideas for Side App"
            Date = DateTime(2026, 2, 12)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Development", "http://example.com/ontology/development")
                        OntologyAnnotation("Software", "http://example.com/ontology/software")
                    ]
                )
            Content =
                "I have been thinking about building a lightweight note search engine. The app should support tagging and full text search. It would be nice to experiment with fuzzy matching. Maybe I can implement ranking based on keyword frequency. I should also consider how to store notes efficiently. Performance testing will be important once the dataset grows. A small UI prototype in Feliz could help validate the concept."
        }
        {
            RelativePath = "notes/2026-02-14/Workout_Routine_Update/Workout_Routine_Update.md"
            Title = "Workout Routine Update"
            Date = DateTime(2026, 2, 14)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Fitness", "http://example.com/ontology/fitness")
                        OntologyAnnotation("Health", "http://example.com/ontology/health")
                        OntologyAnnotation("Routine", "http://example.com/ontology/routine")
                    ]
                )
            Content =
                "This week I want to adjust my workout schedule. Strength training should be prioritized over cardio. I will focus on compound lifts like squats and deadlifts. Rest days are important for recovery. Tracking progress in a simple log could help. Nutrition also plays a major role in performance. I should increase protein intake slightly."
        }
        {
            RelativePath = "notes/2026-02-16/Books_to_Read/Books_to_Read.md"
            Title = "Books to Read"
            Date = DateTime(2026, 2, 16)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Education", "http://example.com/ontology/education")
                        OntologyAnnotation("Reading", "http://example.com/ontology/reading")
                    ]
                )
            Content =
                "There are several books I want to read this year. I am especially interested in software architecture topics. Clean code practices are always worth revisiting. I also want to explore a few science fiction novels. Reading before bed helps reduce screen time. Maybe I should join an online book club. Keeping short summaries of each book would help retention."
        }
        {
            RelativePath = "notes/2026-02-18/Travel_Planning/Travel_Planning.md"
            Title = "Travel Planning"
            Date = DateTime(2026, 2, 18)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Travel", "http://example.com/ontology/travel")
                        OntologyAnnotation("Leisure", "http://example.com/ontology/leisure")
                        OntologyAnnotation("Budget", "http://example.com/ontology/budget")
                    ]
                )
            Content =
                "I am considering a short trip during the summer. A quiet place near the ocean sounds relaxing. Budget planning needs to be done in advance. I should look for affordable flights soon. Packing light will make travel easier. It would be nice to explore local food markets. Taking plenty of photos is a must."
        }
        {
            RelativePath = "notes/2026-02-20/Learning_Goals/Learning_Goals.md"
            Title = "Learning Goals"
            Date = DateTime(2026, 2, 20)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Learning", "http://example.com/ontology/learning")
                        OntologyAnnotation("FunctionalProgramming", "http://example.com/ontology/functionalprogramming")
                        OntologyAnnotation("Goals", "http://example.com/ontology/goals")
                    ]
                )
            Content =
                "This month I want to deepen my knowledge of functional programming. Practicing F# daily will help reinforce concepts. I should review discriminated unions and pattern matching. Building small sample projects is better than only reading theory. Understanding performance tradeoffs is also important. Writing blog posts about what I learn could clarify my thinking. Consistency matters more than intensity."
        }
        {
            RelativePath = "notes/2026-02-22/Home_Office_Improvements/Home_Office_Improvements.md"
            Title = "Home Office Improvements"
            Date = DateTime(2026, 2, 22)
            Tags =
                Some(
                    ResizeArray [
                        OntologyAnnotation("Productivity", "http://example.com/ontology/productivity")
                        OntologyAnnotation("HomeOffice", "http://example.com/ontology/homeoffice")
                    ]
                )
            Content =
                "My home office setup could use some improvements. A better chair would improve posture during long coding sessions. Cable management is currently a mess. Adding a small plant might make the space more inviting. Proper lighting reduces eye strain. I should reorganize the desk drawers this weekend. A second monitor might increase productivity."
        }
    ]
