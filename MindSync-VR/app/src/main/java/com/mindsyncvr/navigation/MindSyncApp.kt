package com.mindsyncvr.navigation

import androidx.compose.foundation.layout.*
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.mindsyncvr.MindSyncActions
import com.mindsyncvr.core.design.Midnight
import com.mindsyncvr.core.design.Teal
import com.mindsyncvr.core.design.TextMuted
import com.mindsyncvr.core.model.AppState
import com.mindsyncvr.features.analytics.AnalyticsScreen
import com.mindsyncvr.features.auth.LoginScreen
import com.mindsyncvr.features.auth.SignUpScreen
import com.mindsyncvr.features.auth.WelcomeScreen
import com.mindsyncvr.features.dashboard.HomeScreen
import com.mindsyncvr.features.onboarding.OnboardingScreen
import com.mindsyncvr.features.questionnaire.QuestionnaireHistoryScreen
import com.mindsyncvr.features.questionnaire.QuestionnairesScreen
import com.mindsyncvr.features.session.LiveSessionScreen
import com.mindsyncvr.features.session.PreSessionScreen
import com.mindsyncvr.features.session.ReadyScreen
import com.mindsyncvr.features.session.SessionCompleteScreen
import com.mindsyncvr.features.settings.SettingsScreen
import com.mindsyncvr.features.settings.SupportScreen
import com.mindsyncvr.features.vr.VrScreen
import com.mindsyncvr.features.wearable.WearableDetailScreen
import com.mindsyncvr.features.wearable.WearableScreen

@Composable
fun MindSyncApp(
    state: AppState,
    actions: MindSyncActions,
    navController: NavHostController = rememberNavController()
) {
    val currentRoute = navController.currentBackStackEntryAsState().value?.destination?.route
    val bottomRoutes = setOf(Routes.Home, Routes.Questionnaires, Routes.Analytics, Routes.Settings)

    Column(Modifier.fillMaxSize()) {
        Box(Modifier.weight(1f)) {
            NavHost(navController = navController, startDestination = Routes.Welcome) {
                composable(Routes.Welcome) { WelcomeScreen(onLogin = { navController.navigate(Routes.Login) }, onSignUp = { navController.navigate(Routes.SignUp) }) }
                composable(Routes.Login) { LoginScreen(actions = actions, onDone = { navController.navigate(Routes.Onboarding) { popUpTo(Routes.Welcome) { inclusive = true } } }) }
                composable(Routes.SignUp) { SignUpScreen(actions = actions, onDone = { navController.navigate(Routes.Onboarding) { popUpTo(Routes.Welcome) { inclusive = true } } }) }
                composable(Routes.Onboarding) { OnboardingScreen(state = state, actions = actions, onDone = { navController.navigate(Routes.Home) { popUpTo(Routes.Onboarding) { inclusive = true } } }) }
                composable(Routes.Home) { HomeScreen(state = state, navigate = navController::navigate) }
                composable(Routes.Wearable) { WearableScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.WearableDetail) { WearableDetailScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.Vr) { VrScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.PreSession) { PreSessionScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.Ready) { ReadyScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.Live) { LiveSessionScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.Complete) { SessionCompleteScreen(navigate = navController::navigate) }
                composable(Routes.Questionnaires) { QuestionnairesScreen(state = state, actions = actions, navigate = navController::navigate) }
                composable(Routes.QuestionnaireHistory) { QuestionnaireHistoryScreen(state = state) }
                composable(Routes.Analytics) { AnalyticsScreen(state = state) }
                composable(Routes.Settings) { SettingsScreen(navigate = navController::navigate) }
                composable(Routes.Support) { SupportScreen(navigate = navController::navigate) }
            }
        }
        if (currentRoute in bottomRoutes) {
            NavigationBar(containerColor = Midnight) {
                listOf(
                    Routes.Home to "Home",
                    Routes.Questionnaires to "Validate",
                    Routes.Analytics to "Trends",
                    Routes.Settings to "Settings"
                ).forEach { (route, label) ->
                    NavigationBarItem(
                        selected = currentRoute == route,
                        onClick = { navController.navigate(route) },
                        label = { Text(label) },
                        icon = { Text("•") },
                        colors = androidx.compose.material3.NavigationBarItemDefaults.colors(
                            selectedIconColor = Teal,
                            selectedTextColor = Teal,
                            unselectedIconColor = TextMuted,
                            unselectedTextColor = TextMuted
                        )
                    )
                }
            }
        }
    }
}
