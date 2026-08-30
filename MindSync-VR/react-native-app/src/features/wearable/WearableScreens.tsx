import React, {useState} from 'react';
import {Text, View} from 'react-native';
import {Bluetooth, BluetoothSearching, Link2Off, RotateCw, Server} from 'lucide-react-native';
import {Card, Field, Header, Metric, PrimaryButton, Screen, SecondaryButton, SectionHeader, StatusPill, uiStyles} from '../../components/ui';
import {WEARABLE_DEVICE_NAME, WEARABLE_RAW_PPG_UUID, WEARABLE_SERVICE_UUID, WEARABLE_TELEMETRY_UUID} from '../../services/ble/wearableBleService';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

export function WearableScreen({navigation}: any) {
  const devices = useMindSyncStore(state => state.wearableDevices);
  const status = useMindSyncStore(state => state.wearableState);
  const error = useMindSyncStore(state => state.ble.lastError);
  const logs = useMindSyncStore(state => state.ble.logs);
  const scan = useMindSyncStore(state => state.scanWearables);
  const connect = useMindSyncStore(state => state.connectWearable);
  return (
    <Screen>
      <Header title="Connect wearable" subtitle="Scan for the ESP32-S3 physiological sensor used in this session." onBack={navigation.goBack} />
      <Card>
        <Text style={uiStyles.value}>ESP32-S3 target</Text>
        <Text style={uiStyles.body}>Device name: {WEARABLE_DEVICE_NAME}</Text>
        <Text style={uiStyles.label}>Service: {WEARABLE_SERVICE_UUID}</Text>
        <Text style={uiStyles.label}>Telemetry: {WEARABLE_TELEMETRY_UUID}</Text>
        <Text style={uiStyles.label}>Raw PPG: {WEARABLE_RAW_PPG_UUID}</Text>
        <Text style={uiStyles.label}>JSON notifications at approximately 5 Hz</Text>
      </Card>
      <PrimaryButton label={status === 'scanning' ? 'Scanning nearby devices' : 'Scan wearable'} loading={status === 'scanning'} icon={BluetoothSearching} onPress={() => void scan()} />
      {error ? <Card><Text style={[uiStyles.value, {color: colors.rose}]}>BLE issue</Text><Text style={uiStyles.body}>{error}</Text></Card> : null}
      {devices.length ? <SectionHeader title="Nearby candidates" subtitle="Verified wearable advertisements are shown first. Anonymous candidates are verified after connection." /> : null}
      {devices.map((device, index) => (
        <Card key={device.id}>
          <View style={uiStyles.rowBetween}>
            <View style={{flex: 1}}><Text style={uiStyles.value}>{device.name}{index === 0 ? ' · strongest' : ''}</Text><Text style={uiStyles.label}>{device.id}</Text></View>
            <StatusPill label={`${device.rssi} dBm`} tone={device.rssi > -70 ? 'good' : 'warning'} />
          </View>
          <Text style={uiStyles.body}>{device.verified ? 'Wearable advertisement verified' : 'Advertisement unverified; GATT service will be checked before connection is accepted.'}</Text>
          <SecondaryButton label="Connect and verify" icon={Bluetooth} onPress={() => { void connect(device); navigation.navigate('WearableDetail'); }} />
        </Card>
      ))}
      <Card><Text style={uiStyles.value}>Scan logs</Text>{logs.length ? logs.slice(0, 10).map((line, index) => <Text key={`${line}-${index}`} style={uiStyles.label}>{line}</Text>) : <Text style={uiStyles.body}>Tap scan to inspect nearby BLE advertisements.</Text>}</Card>
    </Screen>
  );
}

export function WearableDetailScreen({navigation}: any) {
  const device = useMindSyncStore(state => state.selectedWearable);
  const status = useMindSyncStore(state => state.wearableState);
  const ble = useMindSyncStore(state => state.ble);
  const connect = useMindSyncStore(state => state.connectWearable);
  const disconnect = useMindSyncStore(state => state.disconnectWearable);
  const componentB = useMindSyncStore(state => state.componentB);
  const setComponentBEndpoint = useMindSyncStore(state => state.setComponentBEndpoint);
  const [componentBEndpoint, setComponentBEndpointInput] = useState(componentB.endpoint);
  const t = ble.telemetry;
  const tone = status === 'connected' ? 'good' : status === 'error' ? 'danger' : 'warning';
  const componentBTone = componentB.connectionState === 'connected' ? 'good' : componentB.connectionState === 'error' ? 'danger' : 'warning';
  return (
    <Screen>
      <Header title="Wearable detail" subtitle="Live sensor readiness and device metadata." onBack={navigation.goBack} />
      <Card>
        <Text style={uiStyles.value}>{device?.verified ? WEARABLE_DEVICE_NAME : device?.name ?? 'No wearable selected'}</Text>
        <StatusPill label={status.replace('-', ' ')} tone={tone} />
        <Text style={uiStyles.body}>ESP32-S3 Mini · {device?.id ?? 'identifier unavailable'}</Text>
        <View style={uiStyles.row}><Metric label="PPG" value={t?.ir !== null && t?.ir !== undefined ? 'Live' : '--'} accent={t?.ir ? colors.green : colors.muted} /><Metric label="noise" value={t?.noiseAverage !== null && t?.noiseAverage !== undefined ? 'Live' : '--'} accent={t?.noiseAverage !== null ? colors.green : colors.muted} /><Metric label="battery" value={t?.batteryPercent ?? '--'} /></View>
      </Card>
      <Card>
        <View style={uiStyles.rowBetween}><Text style={uiStyles.value}>Live telemetry</Text><StatusPill label={ble.isStreaming ? 'Streaming' : 'Waiting'} tone={ble.isStreaming ? 'good' : 'warning'} /></View>
        <View style={uiStyles.row}><Metric label="IR" value={t?.ir ?? '--'} accent={colors.teal} /><Metric label="RED" value={t?.red ?? '--'} accent={colors.violet} /></View>
        <View style={uiStyles.row}><Metric label="noise average" value={t?.noiseAverage ?? '--'} /><Metric label="noise peak" value={t?.noisePeak ?? '--'} /></View>
        <Text style={uiStyles.body}>Temperature: {t?.temperatureC !== null && t?.temperatureC !== undefined ? `${t.temperatureC.toFixed(2)} C` : 'Unavailable'}</Text>
        <Text style={uiStyles.label}>Packets received: {ble.telemetryCount}</Text>
        <Text style={uiStyles.label}>Latest phone receipt: {t ? new Date(t.receivedAt).toLocaleTimeString() : '--'}</Text>
      </Card>
      <Card>
        <Text style={uiStyles.value}>Derived measurements</Text>
        <Text style={uiStyles.body}>Heart rate: {t?.heartRateBpm ?? 'Unavailable'}</Text>
        <Text style={uiStyles.body}>RR interval: {t?.rrIntervalMs ?? 'Unavailable'}</Text>
        <Text style={uiStyles.body}>SpO2: {t?.spo2 ?? 'Unavailable'}</Text>
        <Text style={uiStyles.body}>Battery: {t?.batteryPercent ?? 'Unavailable'}</Text>
      </Card>
      <Card>
        <View style={uiStyles.rowBetween}>
          <Text style={uiStyles.value}>Component B relay</Text>
          <StatusPill label={componentB.connectionState.replace('-', ' ')} tone={componentBTone} />
        </View>
        <View style={uiStyles.row}>
          <Metric label="frame" value={`${componentB.frameSamplesBuffered}/960`} accent={colors.cyan} />
          <Metric label="raw samples" value={componentB.rawSamplesReceived} accent={colors.teal} />
          <Metric label="accepted" value={componentB.framesAcknowledged} accent={colors.green} />
        </View>
        <Text style={uiStyles.body}>Raw BLE stream: {componentB.rawCharacteristicAvailable ? 'Available' : 'Unavailable'}</Text>
        <Text style={uiStyles.label}>Frames sent: {componentB.framesSent} · queued: {componentB.framesQueued}</Text>
        {componentB.lastBackendMessage ? <Text style={uiStyles.label}>{componentB.lastBackendMessage}</Text> : null}
        <Field
          label="Component B ingest WebSocket"
          value={componentBEndpoint}
          onChangeText={setComponentBEndpointInput}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
        />
        <SecondaryButton label="Apply backend endpoint" icon={Server} onPress={() => setComponentBEndpoint(componentBEndpoint)} />
        {componentB.lastError ? <Text style={[uiStyles.body, {color: colors.rose}]}>{componentB.lastError}</Text> : null}
      </Card>
      {ble.lastError ? <Card><Text style={[uiStyles.value, {color: colors.rose}]}>Last error</Text><Text style={uiStyles.body}>{ble.lastError}</Text></Card> : null}
      {device && status !== 'connected' ? <PrimaryButton label="Reconnect" icon={RotateCw} onPress={() => void connect(device)} /> : null}
      <SecondaryButton label="Disconnect wearable" danger icon={Link2Off} onPress={() => void disconnect()} />
      <Card><Text style={uiStyles.value}>Recent BLE lifecycle</Text>{ble.logs.slice(0, 8).map((line, index) => <Text key={`${line}-${index}`} style={uiStyles.label}>{line}</Text>)}</Card>
      <Card><Text style={uiStyles.value}>Component B lifecycle</Text>{componentB.logs.length ? componentB.logs.slice(0, 8).map((line, index) => <Text key={`${line}-${index}`} style={uiStyles.label}>{line}</Text>) : <Text style={uiStyles.body}>Waiting for the wearable connection.</Text>}</Card>
    </Screen>
  );
}
