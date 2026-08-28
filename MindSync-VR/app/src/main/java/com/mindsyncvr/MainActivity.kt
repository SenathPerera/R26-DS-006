package com.mindsyncvr

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import com.mindsyncvr.core.design.MindSyncTheme
import com.mindsyncvr.navigation.MindSyncApp

class MainActivity : ComponentActivity() {
    private val viewModel: MindSyncViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            val state by viewModel.state.collectAsState()
            MindSyncTheme {
                MindSyncApp(state = state, actions = viewModel)
            }
        }
    }
}
