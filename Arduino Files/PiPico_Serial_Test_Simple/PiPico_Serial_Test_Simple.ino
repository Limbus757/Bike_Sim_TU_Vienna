// This is an example Arduino sketch to send a string of data over the serial port.
// It's designed to work with the Unity C# script for serial communication.

// Declare your variables
float speed = 10.5;
float steering_angle = -15.2;
float front_brake = 0.8;
float back_brake = 0.5;
float resistance = 2.0;
float pitch = 5.0;
float roll = -3.0;

void setup() {
  // Initialize the serial communication at a specific baud rate.
  // This must match the baud rate you use in your Unity C# script.
  Serial.begin(9600);

  // Optional: Add a delay to give the Serial Monitor time to connect.
  delay(100);
}

void loop() {
  // Use a String object to build the data packet.
  // Using String concatenation is memory-intensive but simple for this example.
  String dataPacket = String(speed, 2); // Print with 2 decimal places

  // Send the full string followed by a newline character.
  // The newline is crucial for the Unity script to know when the message ends.
  Serial.println(dataPacket);
  
  // Optional: Print to the Serial Monitor for debugging
  // Serial.print("Sent: ");
  // Serial.println(dataPacket);

  // Wait for a short period before sending the next packet
  delay(100);
}
