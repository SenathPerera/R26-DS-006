package com.mindsyncvr.core.data

import com.mindsyncvr.core.model.*

object MockData {
    val demoUser = UserProfile(
        id = "user_demo_001",
        email = "participant@mindsync.local",
        name = "Ari"
    )

    val devices = listOf(
        WearableDevice("ble-aurora-01", "MindSync Band A1", -48, 84, "1.8.2", "Today", 91, 88),
        WearableDevice("ble-calm-02", "BioSense Calm Patch", -66, 62, "0.9.7", "Today", 76, 79),
        WearableDevice("ble-lab-03", "Lab HRV Sensor", -72, 51, "2.1.0", "Yesterday", 69, 72)
    )

    val researchState = ResearchComponentState(
        signalConfidence = 88,
        sensorQuality = "Stable PPG + HRV window",
        stressLevel = 34,
        stressBand = StressBand.Balanced,
        stressSummary = "Balanced with mild activation",
        vrAdaptationState = "Softening visual density",
        environmentProfile = "Ocean dusk",
        personalizationStatus = "Matched to low-motion preference",
        validationPending = true,
        validationCompletion = "Not started",
        audioPersonalizationActive = true,
        soundAdaptationLevel = 42,
        ambientBlendingState = "Warm pads with low nature layer",
        therapeuticAudioMode = "Guided breath pacing"
    )

    val sessions = listOf(
        MeditationSession("s-104", "Ocean Dusk Reset", 18, "Ocean", "Warm pads", 100, 4, 8, true, "Good relaxation response, no discomfort reported."),
        MeditationSession("s-103", "Forest Grounding", 12, "Forest", "Nature heavy", 96, 5, 7, true),
        MeditationSession("s-102", "Abstract Calm Focus", 10, "Abstract calm", "Soft drone", 93, 6, 7, false)
    )

    val questionnaires = listOf(
        QuestionnaireTemplate(
            id = "pre-stress-v1",
            title = "Pre-session check-in",
            description = "A short baseline before your meditation begins.",
            component = "pre_session",
            version = "1.0",
            questions = listOf(
                QuestionnaireQuestion("stress_now", "How much stress do you notice right now?", QuestionType.Slider, required = true, min = 0, max = 10),
                QuestionnaireQuestion("mood_now", "How would you rate your current mood?", QuestionType.Likert, required = true, min = 1, max = 7),
                QuestionnaireQuestion("body_state", "Which body state feels most present?", QuestionType.SingleChoice, required = true, options = listOf("Settled", "Tense", "Tired", "Restless", "Neutral"))
            )
        ),
        QuestionnaireTemplate(
            id = "component-d-post-v1",
            title = "Post-session validation",
            description = "Component D validation for relaxation, immersion, safety, and perceived benefit.",
            component = "component_d",
            version = "1.0",
            questions = listOf(
                QuestionnaireQuestion("relaxation_after", "How relaxed do you feel now?", QuestionType.Slider, required = true, min = 0, max = 10),
                QuestionnaireQuestion("immersion", "How immersive did the VR environment feel?", QuestionType.Likert, required = true, min = 1, max = 7),
                QuestionnaireQuestion("audio_fit", "The audio felt supportive for my current state.", QuestionType.Likert, required = true, min = 1, max = 7),
                QuestionnaireQuestion("discomfort", "Did you experience discomfort?", QuestionType.SingleChoice, required = true, options = listOf("No", "Mild", "Moderate", "Strong")),
                QuestionnaireQuestion("discomfort_detail", "What discomfort did you notice?", QuestionType.MultipleChoice, options = listOf("Dizziness", "Eye strain", "Audio sensitivity", "Emotional discomfort", "Motion sensitivity"), branch = BranchRule("discomfort", equals = "Moderate")),
                QuestionnaireQuestion("notes", "Anything you would like the research team to understand?", QuestionType.Text)
            )
        ),
        QuestionnaireTemplate(
            id = "adverse-report-v1",
            title = "Discomfort report",
            description = "Use this if anything felt unsafe, too intense, or uncomfortable.",
            component = "adverse",
            version = "1.0",
            questions = listOf(
                QuestionnaireQuestion("severity", "How intense was the experience?", QuestionType.SingleChoice, required = true, options = listOf("Mild", "Moderate", "Strong")),
                QuestionnaireQuestion("support_needed", "Would you like a follow-up from the study team?", QuestionType.SingleChoice, required = true, options = listOf("Yes", "No")),
                QuestionnaireQuestion("description", "Describe what happened in your own words.", QuestionType.Text, required = true)
            )
        )
    )
}
