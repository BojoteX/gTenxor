#include <Servo.h>

// Author: Jesus Altuve
// Date 6/13/2023 @ 21:43 GMT -4

Servo left, right;
#define RIGHT 9
#define LEFT 10

// Misc options
#define FORCE_PROCESSING_STATE 0  // Set the Device in exclusive processing mode (For DEBUG only)
#define BAUD_RATE 115200            // Serial port speed
#define DEBUG 0                   // Enable Debugging

// Some of my Constants
const char QueryState[]         = "KNOCK-KNOCK";
const char HandShakeSnd[]       = "Tenxor_OK";
const char HandShakeRcv[]       = "BINGO";
const char uniqueID[]           = "PUTINESGAY";
const char uniqueIDresponse[]   = "ACK";

// Other responses..
const char SWITCH_2_HANDSHAKE[] = "Changing mode to HANDSHAKE";
const char HANDSHAKE_ACTIVE[]   = "HANDSHAKE is the active mode";
const char PROCESSING_ACTIVE[]  = "PROCESSING is the active mode";

// Servo Related controls
const int MIN_PWM_WIDTH         = 500; // microseconds
const int MAX_PWM_WIDTH         = 2500; // microseconds
const byte MIN_SERVO_ANGLE      = 0;
const byte MAX_SERVO_ANGLE      = 180;
const byte ORIGINAL_TMAX        = 180;
const byte CHANGE_STATE_SIGNAL  = 255;
const byte CURRENT_STATE_SIGNAL = 254;

const int switchPin1            = 2;
const int switchPin2            = 4; // Should have been 3 (my bad)

// Important values
byte ladd=2, radd=3; // Initial ones, but I should send from my Plugin at least ONCE
bool reverseLeft, reverseRight;

enum State {
  HANDSHAKE,
  PROCESSING
};

State currentState = HANDSHAKE;

void setup() {
  pinMode(switchPin1, INPUT_PULLUP);
  pinMode(switchPin2, INPUT_PULLUP);

  // Just allow Pull-up Resistor to stabilize before reading them below
  delayMicroseconds(10);
    
  reverseLeft = digitalRead(switchPin1);
  reverseRight = digitalRead(switchPin2);

  left.attach(LEFT, MIN_PWM_WIDTH, MAX_PWM_WIDTH);
  right.attach(RIGHT, MIN_PWM_WIDTH, MAX_PWM_WIDTH);

  byte left_angle = reverseLeft == true ? MAX_SERVO_ANGLE : MIN_SERVO_ANGLE;
  byte right_angle = reverseRight == true ? MAX_SERVO_ANGLE : MIN_SERVO_ANGLE;

  left.write(left_angle);
  right.write(right_angle);
    
  Serial.begin(BAUD_RATE);
  while (!Serial)
    delayMicroseconds(1);
}

void QueryCurrentState(State cstate, char* _state) {
  if (cstate == HANDSHAKE)
    strcpy(_state, HANDSHAKE_ACTIVE);
  else if (cstate == PROCESSING)
    strcpy(_state, PROCESSING_ACTIVE);
}

void handshakeLoop() {
  while (Serial.available() > 0) {
    byte cmd = Serial.read();
    delayMicroseconds(10); //This delay is not typically necessary, consider removing it
    char str[64] = {0}; // initialize the buffer with null characters
    // This function reads characters from the serial buffer into str until it encounters '\n', or until it has read sizeof(str) - 1 characters.
    if (cmd == CHANGE_STATE_SIGNAL) {
      Serial.readBytesUntil('\n', str, sizeof(str));
      if (strcmp(str, uniqueID) == 0) {
        currentState = PROCESSING;
        Serial.println(uniqueIDresponse);
      }
      else if (strcmp(str, HandShakeSnd) == 0) {
        currentState = HANDSHAKE;
        Serial.println(HandShakeRcv);
      }
    }
    else if(cmd == CURRENT_STATE_SIGNAL) {
      Serial.readBytesUntil('\n', str, sizeof(str));
      if (strcmp(str, QueryState) == 0) {
        currentState = HANDSHAKE;
        char res[32];  // Create a C-string with enough space
        QueryCurrentState(currentState, res);  // Store the result in `res`
        Serial.println(res);
      }
    }
  }
}

void processingLoop() {
  byte received;
  if (Serial.available() > 0) {
    received = Serial.read();
    if (received >= 254) {
      char str[64] = {0}; // initialize the buffer with null characters
      if(received == CURRENT_STATE_SIGNAL) {
        Serial.readBytesUntil('\n', str, sizeof(str));
        if (strcmp(str, QueryState) == 0) {
          currentState = PROCESSING;
          char res[32];  // Create a C-string with enough space
          QueryCurrentState(currentState, res);  // Store the result in `res`
          Serial.println(res);
        }
      }
      else if(received == CHANGE_STATE_SIGNAL && !FORCE_PROCESSING_STATE) {
        Serial.readBytesUntil('\n', str, sizeof(str));
        if (strcmp(str, uniqueID) == 0) {
          currentState = PROCESSING;
          Serial.println(uniqueIDresponse);
          char res[32];  // Create a C-string with enough space
          QueryCurrentState(currentState, res);  // Store the result in `res`
        }
        else {
          currentState = HANDSHAKE;
          Serial.println(SWITCH_2_HANDSHAKE);
        }
      }
      return;
    }
    else if (received < 2) {
      byte add;
      while (Serial.available() == 0)
        delay(1);
      add = Serial.read();    
      if (received & 1)
        radd = min(max(add, 0), ORIGINAL_TMAX);
      else 
        ladd = min(max(add, 0), ORIGINAL_TMAX);
    } else {      
      // Read the other value (it will be the right angle)
      while (Serial.available() == 0)
        delay(1);
      byte received_new = Serial.read();

      // Calculate maximum allowable offsets (we don't want them overflowing)
      byte max_radd = ORIGINAL_TMAX - received_new;
      byte max_ladd = ORIGINAL_TMAX - received;

      // Clip radd and ladd to their maximum allowable values 
      radd = min(radd, max_radd);
      ladd = min(ladd, max_ladd);

      // Calculate our final angles/positions
      byte r_angle = received_new + radd;
      byte l_angle = received + ladd;

      // Reverse the servos based on pull up switches and clamp between 0 and ORIGINAL_TMAX
      r_angle = reverseRight ? ORIGINAL_TMAX - r_angle : r_angle;
      r_angle = max((byte)0, min(r_angle, ORIGINAL_TMAX));

      l_angle = reverseLeft ? ORIGINAL_TMAX - l_angle : l_angle;
      l_angle = max((byte)0, min(l_angle, ORIGINAL_TMAX));

      // Write to our servos!
      left.write(l_angle);
      right.write(r_angle);
    }
  }
} 

void loop() {
  if(FORCE_PROCESSING_STATE == 1)
    currentState = PROCESSING;    
  if (currentState == HANDSHAKE) {
    handshakeLoop();
  } else if (currentState == PROCESSING) {
    processingLoop();
  }
}
